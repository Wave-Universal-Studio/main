# Arquitetura do Wave Studio

## F5-I2C1-OWNED-WASAPI-PACKET-STEPS (em revisão)

`WasapiBusCaptureOwnerStep` e `WasapiBusRenderOwnerStep` são proprietários dos serviços `IWasapiCapturePackets` e `IWasapiRenderPackets` já abertos. Verificam `MTA` e identidade da thread criadora em toda operação, copiam os dados somente enquanto o buffer nativo está adquirido e chamam ReleaseBuffer com o número correto de frames (ou SILENT para render não autorizado/indisponível). Uma falha de liberação é tratada como recurso retido: não descartar o wrapper COM e invalidar a ponte de buffers PCM para impedir publicação tardia.

O filtro de QPC rejeita instantes futuros/antigos/não monotônicos e posições de captura regressivas, além de pacotes de tamanho/flags incorretos. Ele não comprova relógios de dois endpoints sincronizados nem deriva/drift; owner/scheduler nativo futuro precisa aproveitar `WasapiRunningClock` e `WasapiClockAdapter` para a prova temporal completa. Sem `IAudioClient.Start`, sem threads próprias e sem captura/prova física.

## F5-I2B-NATIVE-PCM-PACKET-ADAPTER (integrada)

`WasapiCaptureBlockAdapter` recebe buffers já verificados pelo owner externo e os converte com `PcmFloatConverter` para blocos de 10 ms, aceitando somente PCM16/float32 LE e formatos `WasapiPcm.Validate` (44,1/48 kHz, mono/estéreo). Frames incompletos, endpoints/gerações incorretos, descontinuidade, timestamp ruim ou flags desconhecidas são recusados. SILENT aceita ponteiro nulo/ausência de bytes e gera zeros. Não abre `IAudioClient`, não lê QPC nem libera pacotes de COM; tais garantias pertencem ao owner de captura.

`WasapiRenderBlockAdapter` consome Personal Mix, Stream Mix ou Stream externo apenas por ticket válido do `BoundedBusPcmTransport` e codifica diretamente em `Span<byte>` sem alocação gerenciada por bloco após preparação. As amostras são sanitizadas, limitadas a ±1 e, em PCM16, quantizadas com saturação. Quando não há bloco válido, o destino é limpo e a entrega falha. Quem futuramente copiar para o endpoint físico deve revalidar o ticket imediatamente antes de liberar o render; o adaptador não substitui essa verificação nem inicia monitoramento.

A ponte continua interna: não há clock/scheduler nativo, saída simultânea a dispositivo e OBS nem isolação PCM por sessão Windows. Nenhum driver, dispositivo padrão ou preferência do proprietário é alterado.

## F5-I2A-BOUNDED-PCM-BRIDGE (integrada)

`BoundedBusPcmTransport` implementa `IBusPcmTransport` em produção, mantendo filas SPSC pré-alocadas para cada origem de captura e para as saídas Personal Mix, Stream Mix e, opcionalmente, Stream externo. As fontes são registradas por `TransportEndpoint` (ID/direção/geração), com `PcmFloatFormat` de 44,1/48 kHz, mono ou estéreo; um bloco contém exatamente 10 ms. O produtor oferece PCM com índice de bloco monotonamente crescente; o consumidor do mixer exige o bloco exato e descarta os anteriores. Blocos futuros são conservados. Capacidade é fixa de 2 a 64 blocos, potência de dois; sobrecarga rejeita novo dado sem sobrescrever o que o outro lado ainda não leu.

As filas rejeitam reentrância por lane e não alocam no caminho de dados após a inicialização. As saídas mantêm tickets de geração: invalidação, cancelamento ou fechamento impedem acesso ao PCM, e o consumidor nativo futuro deve verificar o mesmo ticket **novamente** no ponto efetivo de publicação. Personal e Stream têm consumidores independentes. A entrega interna só tenta publicar quando as duas filas possuem espaço; uma falha invalida o ticket, mas isso não configura transação atômica com qualquer dispositivo externo. A fila não introduz relógio ou correção de drift: **quem alimenta o transporte precisa já mapear os pacotes para o mesmo clock de geração**.

Não há ligação a APIs de dispositivos, mudança de padrão Windows, captura persistente, driver virtual ou roteamento OBS. A ponte é uma fundação verificável para futuras fontes/sinks WASAPI, não prova física de F5-I2. [Issue #2](https://github.com/Wave-Universal-Studio/main/issues/2).

## F5-I1-BUS-MATRIX-CORE

`BusMixSession` usa a identidade `TransportEndpoint` do motor e o formato `PcmFloatFormat`. O transporte `IBusPcmTransport` oferece PCM em blocos identificados por fluxo, geração e índice. Ele recebe os dois barramentos e seus medidores em uma entrega interna. A entrega externa opcional recebe somente Stream Mix. O ticket associa entrega, formato, geração e destino autorizado e deve ser conferido na publicação e no consumo de cópias enfileiradas.

Uma geração contém até oito fontes e uma matriz explícita. Cada envio começa ausente até a configuração da topologia. Ganho e mute operam somente sobre envios autorizados. Stream exige consentimento separado para microfone, comunicação, retorno de chamada e origem agregada. Para um destino identificado como chamada, o motor exclui o retorno da própria chamada, comunicação sem identificação de retorno e agregados cujo conteúdo de retorno é indeterminado. Fontes marcadas como saídas de barramento são recusadas. O transporte responde pela procedência correta das fontes e pela identidade do destino.

A sessão injeta transporte sem tocar os runners nativos limitados a cinco segundos. A próxima integração poderá ligar fontes e destinos reais após revisão, autorização e prova de viabilidade. Solo/PFL/AFL pertence a um monitoramento Personal futuro. [Contrato temporal e de processamento](AUDIO_ENGINE_PLAN.md#f5-i1-bus-matrix-core)

## F4-I2-GAMECHAT-SOLO-LANG — balanço e idiomas

O balanço reutiliza a fila de comandos de `ApplicationSessionService`. A identidade é a composição do endpoint, da geração de seleção e de `SessionKey`, independente do PID. O painel requer os dois grupos e todas as faixas neles atribuídas ativas, identificadas e com volume disponível. A faixa de sons do sistema fica fora do balanço. O arraste confirma no fim do gesto. As setas agrupam mudanças por 180 ms e Esc cancela a edição pendente. O gesto fica separado dos faders absolutos da mesa.

`GameChatBalance` captura todos os volumes originais antes da primeira escrita. Uma posição de magnitude `p` preserva o lado favorecido e aplica o fator `1 − p/100` ao volume original do lado oposto. O serviço verifica atividade, identidade, geração e volume esperado, compara no formato `float` usado pelo Windows e lê cada escrita de volta. O centro e a saída restauram condicionalmente o conjunto atenuado. Uma falha entre escritas tenta compensar as alterações confirmadas ou tentadas. Uma nova sessão começa sem grupo. Mudança de grupo, identidade, atividade, disponibilidade ou volume esperado suspende o balanço. A troca de saída espera a restauração antes de invalidar a geração antiga. Um estado inacessível permite nova tentativa pelo centro na mesma geração. A restauração sem confirmação continua visível após a troca e no fechamento. Uma instância expirada libera o controle com aviso, pois o Windows pode preservar seus valores, mas o painel jamais aplica o volume original à sessão substituta. [Persistência de volumes de sessão](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions).

O volume e o mute de sessão do Windows são compartilhados pelos clientes que usam essa sessão. As APIs expõem leitura e escrita separadas, sem comparação e escrita atômicas. A verificação reduz a janela de concorrência, e valores externos observáveis são preservados. Se outro cliente escrever o mesmo escalar ou se o processo for interrompido antes da restauração, a recuperação não é garantida. O balanço nunca aciona mute. A etapa omite Solo para evitar silenciar comunicações de terceiros com essa limitação. [Contrato de volume](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmastervolume) · [Contrato de mute](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmute).

Os recursos satélite `StudioStrings.en-US.resx` e `StudioStrings.es-ES.resx` acompanham o conjunto neutro pt-BR. A aplicação resolve `CurrentUICulture` na abertura e usa o inglês ou espanhol suportado para as variantes correspondentes em Seguir Windows. Mensagens emitidas antes de conhecer a preferência usam pt-BR. Idiomas sem suporte usam pt-BR. Formatos numéricos e persistência JSON continuam independentes da tradução. Nomes de endpoints, processos e caminhos continuam sendo fornecidos pelas fontes originais. [Recursos e fallback do .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-resources-resourcemanager).

## F4-I1-REAL-MIXER — faixas do Windows e idioma

A mesa e a grade usam a mesma seleção e os mesmos snapshots de `ApplicationSessionService`. A reconciliação preserva a faixa pelo par endpoint/instância de sessão. Identidades duplicadas são suprimidas, e a escrita localiza uma única instância ativa ou inativa e verifica sua resposta. Os grupos classificam faixas presentes, são mantidos em memória e não alteram um destino de áudio. A troca de dispositivo invalida a geração e descarta gestos e grupos daquela seleção.

O fader mantém o valor em edição durante a consulta de aproximadamente 1 s. O arraste confirma uma escrita ao soltar, e o teclado agrupa mudanças por 180 ms. O painel serializa a aplicação da mesa e atualiza os controles pelo estado de retorno. O nível das faixas usa a leitura instantânea de `AudioMeterInformation` da própria sessão. Uma amostra atrasada ou indisponível recebe indicação própria. O master usa `EndpointVolumeSession` para volume e mute do Windows e `AudioWatchWorker` para o pico agregado da saída. A mesa amostra a reprodução sem consultar o pico do microfone. [Semântica do pico Core Audio](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudiometerinformation-getpeakvalue).

`StudioStrings.resx` é o recurso neutro pt-BR da interface, incluindo acessibilidade, guias e mensagens próprias de áudio e armazenamento. Uma tradução futura precisa cobrir o conjunto integral antes de ser adicionada à lista de culturas aceitas. A inicialização resolve a cultura da interface antes de criar os controles. A cultura numérica continua sendo a regional do Windows. O esquema 3 acrescenta `language` e migra em memória os esquemas 1 e 2. A gravação usa o mesmo backup e a mesma proteção contra concorrência de configurações. [Recursos e fallback do .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-resources-resourcemanager).

## F3-I3-ROUTE-READINESS-DESKTOP — consulta de compatibilidade

A nova aba usa um inventário de Core Audio e PnP e um serviço de prontidão injetável. A ligação PnP do cabo simples usa a identidade corrigida e a mesma regra de pinos do runner. A+B preserva o contrato dos conectores e exige instâncias distintas. A leitura de instâncias do cabo simples filtra o serviço com `PRESENT` e `DONOTGENERATE`, e os padrões são consultados nos seis papéis/direções. O serviço aplica a validação de formato exato e geração aos endpoints escolhidos e o contrato de rota existente a cada fluxo. O caminho de consulta termina nos metadados de capacidade, antes de inicializar fluxo.

A aba acompanha as notificações de catálogo da janela. Enquanto a aba estiver selecionada, agrupa mudanças com espera breve, cancela a consulta anterior e publica apenas a revisão atual. As escolhas por ID ficam em memória durante a sessão. A perda de um dispositivo invalida o diagnóstico e mantém sua escolha indisponível até escolha explícita ou nova observação. O fechamento sinaliza cancelamento e aguarda a consulta em andamento.

## F3-I2-SINGLE-CABLE — transferência de um cabo

A configuração de quatro endpoints usa o mesmo contrato de pares e tipos da rota A+B, com identidade própria e o mesmo motor. O lote adquire um guard, e cada operação adquire seu lease interno. A contagem de PCM só avança após liberação confirmada. Entre os fluxos, restauração observada e captura de quietude com cadência confirmada são pré-condições de novo consentimento e nova geração. A enumeração de produtores inclui os pinos de render do adaptador e invalida o lote se a evidência se perder. O snapshot privado permite verificar a restauração mesmo se o console fechar. [Roteiro e limites](EXTERNAL_ROUTE_PROOF.md#f3-i2-single-cable--avaliação-com-um-cabo).

No cabo único, a ligação Core Audio localiza o dispositivo conectado. Sua propriedade PnP fornece a chave comum aos pinos do mesmo devnode, mesmo que os subdispositivos tenham IDs de topologia diferentes. A pré-verificação exige pinos saudáveis do driver esperado e usa essa identidade também para o conjunto vigiado de produtores. A resolução roda na pré-verificação, fora do processamento de áudio. [Contrato e evidência](EXTERNAL_ROUTE_PROOF.md#f3-i2-postboot-pairing--diagnóstico-e-correção).

## F3-I1-EXTERNAL-ROUTE — integração externa limitada

Transição F2/F3 autorizada expressamente pelo proprietário em 19/11/2026. As pendências de estabilidade prolongada, recuperação real, latência acústica e necessidade de drift integram a validação futura. O núcleo nominal é compartilhado com a nova fachada, com consentimento ligado à topologia/geração, dois donos MTA e um guard para uma rota por vez. O grafo fixa as duas cadeias direcionadas e exige seis endpoints distintos e dois adaptadores virtuais independentes confirmados pela topologia Core Audio, com seleção e formato exatos. A aplicação cliente configura a rota manualmente. Wave Studio compara o snapshot com a observação do operador, identifica valores elegíveis, conflitos e indisponibilidade, e aguarda quiescência antes da restauração. [Fluxos, limites e aceitação integrada](EXTERNAL_ROUTE_PROOF.md).

## F2-I6D-ALLOC-MONITOR — prova audível limitada

A prova audível tem fachada e consentimento próprios. Usa o núcleo nominal, com encoder disponível só mediante consentimento válido para a rota. Os modos SILENT continuam sem encoder e a UI não chama a nova fachada. [Contrato e roteiro](WASAPI_BACKEND_PLAN.md#f2-i6d-alloc-monitor--roteiro-para-revisão).

## F2-I6C-NOMINAL-MONITOR — fachadas WASAPI por modo

O ciclo de vida dos owners e o guard são comuns. A fachada `WasapiSilentSession` conserva B1/B2 SILENT. `WasapiNominalSession` separa execução nominal, domínio de evidência e relatório. Somente a fábrica de backend simulado dispõe do codificador de render. O modo nativo nominal usa a mesma FIFO e mantém saída SILENT. [Evidências e fronteiras](WASAPI_BACKEND_PLAN.md#f2-i6c-nominal-monitor--transporte-limitado-em-razão-fixa).

## F2-I6B2B-DRIFT — ponte temporal e autoridade do produtor

O checkpoint WASAPI opt-in inclui clock e metadados de pacote publicados pelo mesmo owner que encerrou o lote. Um coordenador combina a faixa de razão entre os clocks com os limites de ocupação e publica um comando expirável vinculado à geração. A captura é proprietária do FIR e aplica o comando entre operações. O render continua SILENT. O ajuste foi exercitado com fakes, e a próxima prova física foi preparada em razão nominal para medir a incerteza que impede habilitar o ajuste no hardware. [Protocolo e critérios da coleta](WASAPI_BACKEND_PLAN.md#f2-i6b2b-drift--controle-com-evidência-intervalar-e-diagnóstico-silent).

## F2-I6B2A-CHECKPOINT — observação da FIFO WASAPI

A sessão silenciosa admite um diário opt-in exercitado com streams injetados. O owner de captura e o de render publicam seus acumulados e intervalos locais em anéis separados. O coordenador aceita o snapshot se as duas versões comprovarem quiescência no mesmo corte, sem pausa dos owners nem chamada COM no coordenador. O ticket vincula a observação à seleção da sessão. A amostragem acompanha os buffers e o guard já existentes. [Protocolo, contabilidade e rejeições](WASAPI_BACKEND_PLAN.md#f2-i6b2a-checkpoint--corte-coerente-dos-dois-owners-mta).

## Implementação atual — F2-I6A, preparação WASAPI sem áudio

Audio/Engine contém interop COM mínimo source-generated do SDK, PreserveSig e wrappers únicos próprios, sem acessar objetos internos NAudio. WasapiPreparation coordena uma operação síncrona MTA completa: endpoint explícito/ativo, mix exato validado, shared Initialize sem conversão/loopback, consultas de buffer/latência e IAudioClock do mesmo cliente, seguido de liberação inversa. Não expõe Start ou buffers PCM. WasapiClockReading preserva HRESULT/ulong e alimenta o WasapiClockAdapter existente; S_FALSE é incerto e não vira Accurate. A leitura parada é apenas evidência de preparação, não observação de drift em execução.

TransportActivityGuard.Shared é usado pela sessão de diagnóstico de produção e preparação; lease só é liberado após término/descarte confirmado. Fakes recebem instâncias isoladas. Cancelamento não aborta COM bloqueado; falha de Release conserva recursos/pais e lease, sem retry em thread diferente. A prova real autorizada inicializou e fechou captura mono/saída LR48kHz sem áudio; buffers/latências são consultas, não latência fim a fim. [ABI, resultados e planejamento F2-I6B](WASAPI_BACKEND_PLAN.md). Sem mudança visível, republicação local ou transporte ativo. DSP/EQ/perfis/JSON e código de UI preservados; Alpha/Fase2 não aceitas.

## Base preservada — EQ-PROFILE-1

EqualizerValues/EqualizerProfile/EqualizerPreferences são snapshots imutáveis de parâmetros, com cópias nos limites e coleção limitada a20. O modelo separa ajuste atual e perfil salvo por ID estável, sem serializar DSP ou áudio. EqualizerPreviewPanel faz binding suprimido e comandos locais; MainForm.EqualizerProfiles consolida gestos/debounce de500 ms e reutiliza a fila de MainForm.Preferences/SettingsStore, com flush após operações de recuperação no fechamento. Nada foi acrescentado ao callback ou ao projeto Audio.

SettingsJson grava schemaVersion2 com equalizer obrigatório, validação estrita e migração explícita de1 em memória (inclusive defaults históricos de WindowPlacement). O original1 permanece baseline/backup até uma gravação posterior; backup1/2 e proteção de futuro/conflito continuam no mesmo SettingsStore. Primeira abertura neutra; ativações persistidas não autorizam áudio. Nível digital permanece20% e clipe vazio após reinício. [Formato, limites e uso](EQ_PREVIEW.md). A seção histórica v1 abaixo descreve o contrato anterior.

## Base preservada — CHECKUP-1

Correções de ciclo de vida sobre a base funcional: engine do diagnóstico invalida época de entrega, separa erros primários/cleanup e só libera recursos após completion ou antes de Start. Session trata falhas de controle/Tick e limita a espera de Dispose, mantendo worker/recursos quando a parada não é confirmada. MainForm.Shutdown acompanha tarefas independentes, captura falhas no async FormClosing e permite nova tentativa sem iniciar disposers concorrentes ou destruir streams ativos. Snapshot do transporte isolado distingue indisponibilidade e invalida entrega. [Evidências, limites e sequência F2-I6A/B](GENERAL_CHECKUP.md). Não há mudança de DSP, backend ativo ou fluxo de áudio global.

## Histórico — EQ-PREVIEW-1

Prova audível pequena no diagnóstico curto: GraphicEqualizerSettings/GraphicEqualizer em Audio/Dsp reutilizam os biquads públicos NAudio, sem projeto/dependência nova. BoundedTestAudio mantém um original selado e cria provider processado com filtros independentes/estado novo por play. Engine/session congelam o snapshot no worker; MicrophoneTestPanel/EqualizerPreviewPanel oferecem dez bandas, preamp/headroom, bypass/reset e comparação original/processado, com ajustes bloqueados durante atividade. Não há novo backend de captura, arquivo processado, monitoramento ou EQ global. [Contrato e teste A/B](EQ_PREVIEW.md). Base WASAPI isolada abaixo preservada; F2-I6 pendente.

## Histórico — F2-I5, contrato WASAPI isolado

Em 14/11/2026, a fundação incorpora WasapiPcm/TransportEndpoint/TransportRoute, probe real de mix/capacidade/períodos em MTA e WasapiClockAdapter testado com posições nativas simuladas. Os snapshots são independentes de COM. Buffer/latência efetivos permanecem desconhecidos, sem Initialize/Start novo ou conversão Windows implícita. Auditoria NAudio 3.1.0 identificou lacuna no HRESULT de precisão do clock; caminho nativo ativo continua bloqueado. [APIs, formatos e limites](WASAPI_BACKEND_PLAN.md).

IsolatedTransportSession executa transporte exclusivamente simulado em worker MTA, com produtor/consumidor separados, FIR adaptativo + SPSC reais, priming contado, spans emprestados e comandos limitados. Gate definitivo por geração é invalidado antes de aguardar callbacks; AdaptiveResampler só recebe Stop/descarte após quiescência, SetTargetPpm/Process somente no produtor. Falhas ficam retidas e recuperação exige nova geração. Média temporal deriva de eventos/contadores fechados por watermark do fake; esse contrato não é atribuído ao Windows. Não há vínculo novo com Desktop/diagnóstico curto, nem stream/monitoramento físico.

TransportActivityGuard prova exclusão com diagnóstico em simulação; integração futura deverá compartilhar a mesma instância entre os dois caminhos. Mute e atenuação digital inicial 0,2 são internos, com opt-in explícito; níveis Windows intactos. Detalhes, provas e próximas condições em WASAPI_BACKEND_PLAN, TEST_PLAN e PROJECT_STATUS. Prévia corrigida republicada somente pelo fluxo autorizado e fechamento seguro; A01–A12 e Windows 11 continuam pendentes. Parar após F2-I5 para revisão.

## Histórico — F2-I4

F2-I4 em 14/11/2026: `AdaptiveResampler` acrescenta processamento de amostras em Audio/Engine, interno e de proprietário serial. Banco FIR interpolado de 257 fases, 161 taps, cursor inteiro/Q48, mono/LR e 48→48/44,1↔48 kHz em ±500 ppm. Mapeamento/sanitização apenas do prefixo aceito, proteção por estágio, razão suavizada sem reset de fase e EOF pela trajetória. Estimador/controlador são conectados ao resampler e à SPSC em ensaios sintéticos com amostras reais em memória; nenhuma integração a dispositivos, UI de DSP ou backend contínuo.

A conversão F2-I2 permanece: ChannelAdapter mono/LR explícito, NominalResampler FIR causal de 161 taps com razão fixa e NominalConversionStage, sanitização antes do histórico e proteção final. Identidade 44,1/48/96 kHz, 44,1↔48 e 96→48; demais pares recusados. SPSC recebe frames no formato de saída; ownership/reset e retorno não transacional de operações em voo continuam conforme [AUDIO_ENGINE_PLAN.md](AUDIO_ENGINE_PLAN.md). Resampling nominal não corrige drift. Metas nominais em [RESAMPLING_QUALITY.md](RESAMPLING_QUALITY.md); simulação/planos adaptativos, custo parcial e diagnóstico de UI em [CLOCK_DRIFT_SIMULATION.md](CLOCK_DRIFT_SIMULATION.md).

375 testes aprovados em Debug/Release (336 anteriores + 39 novos). Geração acompanha comandos/produção; Stop em ReprimeRequired e reset com geração nova impedem continuar usando razão/estimativa antiga. Rejeição cumulativa da janela longa do estimador exige recuperação explícita. Watchdog/pipes possuem prazo e provas próprias; uma falha real de área útil foi corrigida minimamente em MainForm, e o fixture STA histórico passou a usar loop de mensagens assíncrono. Causa completa da intermitência não confirmada. [Contratos, qualidade e limites da F2-I4](ADAPTIVE_RESAMPLING_QUALITY.md). Fase 1 segue em validação física; estratégia global preservada. F2-I5/backend somente após revisão/autorização/regressão, sem monitoramento, EQ ou redesign antecipados.

Identidade oficial incorporada em 13/11/2026: assets/branding mantém original e derivados; ApplicationIcon grava o ICO no recurso Win32 do apphost. StudioBrand carrega ICO/PNG de EmbeddedResource, cria clones independentes dos streams e MainForm os descarta no Dispose. Cabeçalho usa PictureBox Zoom, preservando proporções. Não há I/O de imagem externa no runtime nem alteração no áudio. Metadados e guia em [identidade oficial](../assets/branding/README.md). Distribuição pública e instalador continuam futuros.

Decisões da Fase 0, com atualizações até F1-I6-METER em 13/11/2026, em validação física pendente. Requisitos normativos em [REQUIREMENTS.md](REQUIREMENTS.md). A implementação atual é descrita primeiro, seguida pelo histórico e planejamento de evolução.

## Medidor de entrada na aba de teste — F1-I6-METER

MainForm encaminha o pico de captura do AudioWatchWorker existente a MicrophoneInputMeter, dentro de MicrophoneTestPanel, no timer de 100 ms. A barra anterior de Dispositivos é preservada. Há somente um worker de picos e as mesmas fontes persistentes de endpoint; nenhum stream de gravação ou segundo medidor nativo foi acrescentado. A implementação CoreAudioWatchBackend/AudioMeterInformation.MasterPeakValue permanece igual.

A medição fica ativa em Dispositivos e Testar microfone, pausando nas outras abas e ao minimizar. A visibilidade também é atualizada no evento Shown, após a seleção inicial da aba estar disponível. EnableMetering invalida a época do worker quando muda de estado e publica indisponibilidade imediatamente: uma leitura iniciada antes da pausa não pode reaparecer na retomada. A pausa conserva as fontes existentes na thread MTA e interrompe suas consultas de pico.

MicrophoneInputMeter recebe snapshots, ID de entrada e AudioWatchSelection completo. Só apresenta leituras da seleção/revisão vinculada, com timestamp válido e idade até dois segundos. PeakReading.Stale distingue atraso de indisponibilidade e zero real. Valores inválidos são rejeitados antes de alimentar a barra 0–100%; texto numérico e descrição acessível acompanham o estado, sem depender de cor. A amplitude mostrada não é RMS nem um diagnóstico de voz, inteligibilidade ou zumbido.

Seleção, desconexão/recuperação, suspensão/retomada e fechamento invalidam a apresentação. Callbacks externos apenas sinalizam uma flag atômica; atualização dos controles ocorre na UI. Novo vínculo limpa o valor antes da primeira leitura, incluindo revisões diferentes com o mesmo ID. Encerramento deixa a barra zerada/indisponível e rejeita snapshots tardios. O volume digital para ouvir a gravação segue separado, inicialmente em 20%.

A UI informa que determinados drivers podem exigir captura ativa para informar nível; verificar por Gravar teste, sempre explícito. Não há ajuste automático de privacidade, ganho, mute, formato ou dispositivo padrão. O roteiro registra o relato inicial de zumbido/ausência de voz e o posterior sucesso da gravação/reprodução após ajustes próprios não especificados, separando reação do medidor e confirmação audível, sem inferir causa. Dependências, contratos de captura/reprodução e limites da F1-I6-MIC permanecem preservados.

## Diagnóstico curto de microfone — F1-I6-MIC

Adendo autorizado à Alpha: gravação curta seguida de reprodução por gesto explícito. O motor contínuo, DSP, monitoramento/sidetone, exportação e roteamento externos continuam nas fases posteriores. O microfone mono conserva seu único canal; balanço L/R permanece indisponível.

MicrophoneTestPanel apresenta snapshots e comandos. MicrophoneTestSession possui uma thread MTA dedicada, fila serializada, revisão de alvos e cancelamento de operações. Nenhum objeto COM chega à UI. MicrophoneTestEngine concentra estados e regras testáveis sem hardware. O worker aguarda sem polling quando ocioso e, durante atividade, verifica limites/finalização a cada 25 ms. A UI consulta snapshots a 10 Hz.

CoreAudioMicrophoneTestBackend abre somente IDs selecionados, valida direção/estado/mute e mantém propriedade de cada MMDevice. WasapiRecorder e WasapiPlayer possuem seus clientes/threads internos; criação, comandos e descarte dos wrappers ocorrem na thread proprietária. A conclusão nativa sinaliza flags gerenciadas. O worker libera streams antes dos dispositivos e apaga o clip. Stop da captura aguarda sair de Starting antes de sinalizar parada, evitando a sobrescrita desse sinal pela thread da versão 3.1.0. Não se faz Join nem descarte no callback.

BoundedTestAudio aceita apenas PCM 16 ou float 32, mono/estéreo, 8–96 kHz, com frames completos. Pré-aloca até 10 s, limitado a 8 MiB (máximo atual de 7.680.000 bytes para 96 kHz estéreo float). O callback copia somente o que cabe no buffer, sem disco/rede/UI. A captura utiliza o mix suportado; a reprodução compartilhada usa AutoConvertPcm/SrcDefaultQuality do Windows para adaptar o formato ao destino. Formatos fora do subconjunto ou recusados pela inicialização são informados, sem fallback de endpoint ou alteração de formato padrão.

Após parar, o trecho fica selado e nunca toca automaticamente. Um IWaveProvider.Read(Span<byte>) aplica somente atenuação digital de 0 a 1, inicialmente 0,2; amostras float não finitas viram silêncio e valores fora de escala são limitados. Esse nível não usa SessionVolume, DeviceVolume ou setters de Core Audio. O teste não referencia SettingsStore nem persiste áudio ou seu nível digital.

Mudança de seleção, formato/ciclo de vida, atualização manual, suspensão/retomada e fechamento invalidam a revisão imediatamente. Callbacks e provider verificam revisão/cancelamento, inclusive depois de copiar um pacote. Comandos verificam novamente antes de Start após uma abertura potencialmente lenta. Parar/Limpar cancelam também a operação em abertura. Desconexão/falha de stream descarta o trecho, mesmo antes do catálogo; indisponibilidade detectada ao abrir a saída também descarta. Falha de mute/formato/permissão ao abrir uma saída mantém o trecho para tentativa explícita após conferência. Não há captura e reprodução simultâneas.

APIs e ownership conferidos no código da tag fixada: [WasapiRecorder](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/WasapiRecorder.cs), [builder de captura](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/WasapiRecorderBuilder.cs), [WasapiPlayer](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/WasapiPlayer.cs), [builder de reprodução](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/WasapiPlayerBuilder.cs). A negociação real e o resultado audível aguardam A11. Nenhuma nova dependência ou mudança de lock.

## Correções e medição da prévia — F1-I6

SettingsStore distingue Oversized de Corrupt. ReadOptional verifica o tamanho antes de alocar e após ler o limite conhecido. Arquivo excessivo na carga ou em crescimento durante a gravação invalida baseline, bloqueia save/reset/recuperação e orienta inspeção manual. O principal e o backup permanecem preservados. A serialização de modelo inválido retorna falha compreensível. DrainSettingsSavesAsync e a espera no fechamento tratam falhas previsíveis de validação/I/O. Seletores e handlers aguardam settingsLoaded. O diálogo de aviso aceita injeção nos testes para verificar fechamento após erro.

EndpointPreference.FromSnapshot preserva ausência de controles com erro e evita salvar balanço utilizável com base zero. Um snapshot totalmente indisponível gera mensagem antes de substituir preferências válidas. Na restauração, os canais esperados são obtidos após volume e antes do balanço. O estado final é conferido novamente depois dos demais comandos, inclusive unmute. Tolerância por canal corresponde a 2% da base esperada, preservando sensibilidade em níveis baixos. Base zero retorna resultado incompleto por balanço não observável. A prévia de unmute avisa que a confirmação pode tornar áudio audível.

ApplicationSessionSnapshot acrescenta PeakReading e timestamp monotônico opcionais. CoreAudioSessionEndpoint consulta AudioSessionControl.AudioMeterInformation.MasterPeakValue na operação MTA que já enumera as sessões, copia somente valores e descarta medidor emprestado/volume antes do controle proprietário. A coleção/manager continua pertencendo ao dispositivo. Erro do medidor afeta sua leitura e preserva controles suportados da sessão. Propriedade/descarte foram conferidos na tag fixada. [Medidor NAudio 3.1.0](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioMeterInformation.cs), [controle de sessão](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioSessionControl.cs).

O mixer apresenta Pico (%) · 1 s, atualizando a célula da mesma SessionKey com a geração vigente. A frequência aproveita o polling existente de 1 Hz. Aba oculta, escrita ou edição pausam a consulta periódica, e valores com mais de dois segundos indicam atraso. Silêncio, indisponibilidade e pausa têm estados próprios. Essa amostragem pode perder eventos breves. O ciclo usa wrappers locais a cada reconciliação, mantendo o descarte na mesma thread e a frequência moderada. Uma eventual animação de 10 Hz exigirá fontes persistentes por sessão e nova avaliação.

A medição de 100 consultas completas do mixer observou média de 1,68 ms com quatro sessões reais e picos zero nesta máquina. Os testes STA exercitam pico, foco, pausa, atraso e geração antiga. Preferências duráveis de sessão continuam aguardando identidade inequívoca. A resposta física dos picos com áudio ativo é critério de aceitação pendente.

A publicação framework-dependent gera apphost Windows AMD64 a partir do SDK Windows x64 e PlatformTarget x64, com arquitetura conferida no cabeçalho PE. Saída em artifacts/alpha-preview/win-x64, dependente de Microsoft.NETCore.App e Microsoft.WindowsDesktop.App 10 x64, com avisos dos componentes. Dados de ensaio e configurações temporárias ficam fora do conteúdo versionável. A F1-I6 mantém validação física/visual pendente. [Aceitação e publicação](ALPHA_ACCEPTANCE.md), [componentes](THIRD_PARTY_NOTICES.md).

## Histórico — persistência e restauração explícita da F1-I5

StudioSettings, SettingsJson, WindowPlacement e SettingsStore ficam em Desktop/Settings, com modelos e I/O independentes de WinForms e COM. MainForm.Preferences compõe esses serviços com os controles existentes. O caminho padrão é %LOCALAPPDATA%/WaveStudio/settings.json. Construtor aceita caminho injetado e o executável oferece --settings-file com caminho absoluto para execução isolada. Testes usam diretórios temporários próprios. Dependências e lock files permanecem iguais aos da F1-I4.

schemaVersion 1 é a primeira versão de configuração. Seleções contêm followDefault e endpointId opcional, independentes por direção. EndpointPreference guarda ID, direção e volumePercent/muted/balance opcionais. Campos ausentes expressam ausência de preferência ou suporte. WindowPlacement contém coordenadas, tamanho e maximized, com ajuste à área útil de um monitor disponível. A preferência explícita pode apontar para um ID ausente, por API validada de DeviceSelection, e sobreviver a reinício/retorno. Fallback automático favorece um endpoint ativo quando o padrão marcado está indisponível, informa essa condição e mantém modo automático.

SettingsJson usa System.Text.Json com nomes camelCase, enums em texto, campos conhecidos, rejeição de duplicatas e validação de estrutura, IDs, limites e finitude. UTF-8 com ou sem BOM é aceito. Arquivo limitado a 1 MiB, até 1.000 preferências, IDs com até 2.048 caracteres e profundidade JSON 16. Tamanho de janela válido entre 100 e 16.384 antes do ajuste à área útil. schemaVersion diferente de 1 permanece protegido. Migrações serão acrescentadas quando houver versões anteriores conhecidas a migrar.

```json
{
  "schemaVersion": 1,
  "playback": { "followDefault": true, "endpointId": null },
  "capture": { "followDefault": false, "endpointId": "id-do-endpoint" },
  "window": { "x": 50, "y": 50, "width": 950, "height": 700, "maximized": false },
  "endpoints": [
    { "endpointId": "id-do-endpoint", "direction": "Capture", "volumePercent": 40, "muted": false, "balance": null }
  ]
}
```

Gravações partem de seleção explícita/mudança de modo, Salvar valores atuais ou atualização da geometria no fechamento. A UI coalesce pedidos e SettingsStore serializa o I/O com SemaphoreSlim, executando em Task.Run. Existe prazo de cancelamento de três segundos por gravação e esperas de até cinco segundos para ação de preferência e fila de gravação no fechamento, com aviso se a operação ficar pendente. Chamadas de disco já iniciadas podem terminar antes de observar cancelamento. O fechamento aguarda ações de preferência antes de salvar a janela, evitando regravar um modelo anterior à recuperação/reset.

A política de duas instâncias combina FileShare.None em settings.json.lock durante cada transação com comparação SHA-256 do conteúdo carregado, inclusive imediatamente antes da substituição. Conflito preserva o arquivo atual e pede reabertura. O arquivo de lock pode permanecer vazio, com exclusão determinada pelo handle aberto. Editores externos devem operar com o aplicativo fechado. As verificações de conteúdo detectam alterações externas observáveis durante a transação.

O temporário tem nome exclusivo na mesma pasta e usa FileMode.CreateNew, Flush(true) e File.Replace para substituir o principal, guardando o anterior válido em settings.json.bak. A primeira criação usa File.Move para um destino ainda ausente. Falha de substituição retorna erro, mantendo o arquivo válido. O temporário pertencente à operação é removido no finally. Interrupção de processo pode deixar um temporário órfão para inspeção explícita. O protocolo usa a substituição suportada pelo sistema de arquivos. [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0), [FileStream.Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0).

Corrupção preserva o principal e usa defaults seguros em memória. Backup é validado e oferecido por ação explícita. Recuperação/reset arquiva o original inválido com nome exclusivo e flush antes de substituir o principal, preservando o backup válido. Principal ausente com backup válido também aguarda escolha explícita. Versão incompatível bloqueia save, reset, recuperação e gravação ao fechar. Falha de leitura e conflito mantêm proteção. Configurações maiores que o limite ficam preservadas e exigem inspeção externa antes de recuperação.

Salvar valores atuais faz leitura fresca no serviço do endpoint, com alvo e geração conferidos. Valores salvos aparecem separados dos atuais. Restaurar apresenta alvo/ID, valores atuais/salvos e aviso de aumento, usando confirmação com Não como padrão. EndpointVolumeSession cria EndpointRestorePlan com ID/revisão e estado original. Antes de escrever, a mesma fila reabre o alvo, verifica disponibilidade, suporte e equivalência dos níveis à prévia. Mudança de geração cancela o pedido. Mute ligado é aplicado primeiro, volume e balanço em seguida e unmute por último. Cada comando mantém confirmação e proteção de canais existentes. Erro interrompe os demais comandos e consulta o estado real, indicando possível aplicação parcial. Chamadas nativas já iniciadas podem terminar no alvo original.

Preferências duráveis de sessões permanecem indisponíveis. O identificador geral de sessão pode ser compartilhado por instâncias do mesmo aplicativo, e o identificador de instância identifica a execução específica. A camada atual expõe identidade da instância, PID e metadados. Correspondência durável e inequívoca de aplicativo, endpoint, múltiplas instâncias e sons do sistema requer validação adicional. A configuração atual guarda seleções e preferências de endpoints. [Identificador de sessão](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getsessionidentifier), [identificador de instância](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getsessioninstanceidentifier).

## Recuperação e correções da revisão F1-I4

AudioWatchSnapshot recebe timestamp monotônico, estado de falha e flag de recuperação de controles. A UI invalida picos com mais de dois segundos. Recover invalida imediatamente a leitura e descarta retornos anteriores à sua época. Exceção terminal do loop publica indisponibilidade com orientação para reiniciar. Esse estado encerra o worker e permite descarte ordenado.

Eventos nativos de adição/remoção/estado sinalizam revisão de recuperação atômica. Após coalescência, eles invalidam gerações e falhas latched dos controles/mixer mesmo com ID/estado/formato iguais. Notificações de metadados preservam controles existentes. Retomada e atualização manual reconstroem inscrição/fontes e solicitam recuperação. O evento de catálogo apenas solicita reconciliação, evitando realimentação de Recover. Pedidos recebidos durante consultas de catálogo ou mixer permanecem pendentes até o término da leitura ativa.

Medidores contínuos por sessão continuam na avaliação da F1-I6. O wrapper permite consulta pontual, já verificada. Uma atualização contínua precisa de fontes persistentes por SessionKey, reconciliação e descarte na mesma thread e orçamento medido com várias sessões. A amostra atual de endpoints permanece curta e específica do host.

## Histórico — medidores, notificações e recuperação da F1-I4

AudioWatchWorker possui uma thread MTA dedicada. Nela são construídos, consultados e descartados AudioWatchEngine, CoreAudioWatchBackend, MMDeviceEnumerator, MMDeviceNotificationClient e os dois medidores selecionados. Cada MMDevice é proprietário de AudioMeterInformation e o libera no descarte. Os wrappers permanecem abertos entre ticks. A UI recebe somente AudioWatchSnapshot, PeakReading e IDs/revisões de seleção. A referência do último snapshot é publicada atomicamente.

As APIs públicas conferidas na tag NAudio 3.1.0 e no XML instalado são MMDeviceEnumerator.CreateNotificationClient(useSynchronizationContext: false), seus eventos e AudioMeterInformation.MasterPeakValue. O wrapper público administra IMMNotificationClient e seu registro nativo. Uma inscrição atende adição, remoção, estado e mudança de padrão de qualquer papel/direção. Propriedades são filtradas para nome amigável, nome da interface e formato do engine. Callbacks sinalizam uma flag atômica. O worker agrega eventos com 200 ms de quietude e limite de 1.000 ms por rajada. [Enumerador NAudio](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDeviceEnumerator.cs), [medidor NAudio](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioMeterInformation.cs).

O worker espera 100 ms entre ciclos e mede os dois endpoints ativos. A UI aplica os dados em timer de 100 ms somente quando IDs e revisão coincidem. Selecionar outro alvo invalida a exibição imediatamente. Fontes anteriores são liberadas no próximo ciclo do proprietário. Aba oculta e janela minimizada pausam consultas de pico. Valores finitos de 0 a 1 são aceitos, zero representa silêncio, e valores inválidos ou erros produzem indisponibilidade. Percentual e 20 log10(pico) em dBFS descrevem o pico publicado pelo Windows. RMS, LUFS e detecção de clipping ficam sujeitos a medições específicas futuras.

Falha isolada de leitura fecha a fonte afetada e agenda nova abertura em cinco segundos. Notificações invalidam fontes e antecipam nova consulta. Falha de inscrição é mostrada, com consulta de catálogo e nova tentativa de registro a cada cinco segundos. Atualização manual continua disponível. PowerModeChanged com PowerModes.Resume solicita reconstrução do backend no proprietário e reconciliação do catálogo. A UI serializa consultas de catálogo e retém pedidos recebidos durante uma consulta. Atualizações automáticas preservam controles existentes quando identidade, estado e formato permanecem iguais.

As consultas pontuais de catálogo continuam em CoreAudioDeviceService. Volume de endpoint e sessões continuam nas filas EndpointOperationQueue de seus serviços, com wrappers locais à operação. Essas filas e o worker persistente possuem objetos COM próprios e trocam somente modelos copiados. O callback nativo apenas sinaliza o worker. Encerramento remove o evento público de energia e os assinantes da UI, cancela consultas, drena as filas e aguarda o worker liberar fontes, inscrição e enumerador na própria thread. Chamadas nativas iniciadas podem terminar antes do descarte.

DeviceSelection mantém preferência explícita mesmo que o ID desapareça. A UI mostra indisponibilidade e recupera o mesmo endpoint ao retornar. Inicialmente, cada direção acompanha seu padrão multimídia. Os checkboxes permitem voltar a esse modo. Binding de listas aplica snapshots somente para leitura, preservando o modo automático. Marcas de padrões multimídia e comunicações continuam independentes e baseadas no catálogo real. Atualização, reconexão e retomada consultam os níveis atuais.

O mixer reconcilia DataGridViewRow por SessionKey, atualizando células alteradas e preservando as demais linhas e célula corrente. Edição pausa a aplicação de snapshots. Tag da linha e edição capturam ApplicationSessionBinding com EndpointSelection completo, incluindo a geração. Cancelamento de edição, binding e reescolha do mesmo ID são protegidos contra gerar comandos. Índices são validados antes do acesso. Comando solicita releitura imediata, e seu erro permanece visível nas consultas seguintes. Na busca de uma sessão, falhas ao abrir ou identificar entradas isoladas permitem continuar até um alvo confirmado. Falhas após confirmação do alvo propagam-se.

O medidor de sessão foi consultado pontualmente na integração através de AudioSessionControl.AudioMeterInformation. Medição contínua no mixer exige ampliar o gerenciamento persistente por instância ou medir recriação de wrappers em maior frequência. Essa opção foi adiada para manter o ciclo de vida incremental e verificável. O polling de sessões permanece em 1.000 ms e a sincronização dos controles de endpoint em 600 ms.

Uma amostra de 20 snapshots em 2,21 s observou consulta média de 0,083 ms e picos zero em ambas as direções. O processo completo de testes consumiu delta de 31,2 ms de CPU e working set de 59.949.056 a 61.140.992 bytes. A amostra inclui o runner e descreve esta máquina. Simulação de 11 ticks confirmou duas fontes abertas e 22 leituras, com reutilização e descarte. Ensaios longos, dispositivos adicionais e Windows 11 continuam na matriz física.

Tests passou a referenciar Desktop e habilitar Windows Forms para três regressões STA da grid real. Audio mantém independência de WinForms. Versões NuGet permanecem fixadas. O lock de testes acrescenta somente a referência de projeto.

## Histórico — evolução dos Incrementos 1–3

### Atualização F1-I3

EndpointOperationQueue passou a ser compartilhada pelo controle de endpoint e pelo mixer. Cada geração tem contagem de operações pendentes e é liberada depois de cancelada e drenada. A UI confirma sliders ao fim do gesto, com prévia local durante Scroll, evitando fila de escritas por movimento. Restauração de balanço interrompe-se quando detecta estado externo diferente do original/tentado, informando o usuário. Janelas menores usam rolagem e tamanho limitado à área útil. Essas mudanças sucedem o comportamento original descrito abaixo.

## Mixer de aplicativos — F1-I3

A aba Mixer de aplicativos acompanha a saída selecionada e usa ApplicationSessionService → EndpointOperationQueue → CoreAudioSessionEndpoint. Cada consulta cria seus wrappers no worker MTA, copia snapshots e descarta sessões antes do dispositivo. O dispositivo é proprietário do manager e da coleção. O wrapper SimpleAudioVolume é emprestado da sessão e descartado antes dela. As inscrições internas criadas pelo NAudio são removidas pelo descarte do manager. Notificações persistentes da aplicação ficam para o Incremento 4.

SessionKey combina endpoint e identificador de instância. PID e nome servem como metadados, com fallback para indisponibilidade. Sessões do mesmo PID ficam separadas. Antes de escrever, o serviço reencontra a instância dentro do endpoint, confere sua identidade e estado novamente e verifica cancelamento. Volume é validado em 0–100% e mute é independente. A releitura confirma o resultado. Uma chamada nativa já iniciada pode terminar na sessão original, mesmo após troca de seleção.

Polling de 1.000 ms reconcilia sessões novas, encerradas e alterações externas. A aba oculta ou uma célula em edição pausa consultas periódicas. Erros por sessão geram avisos preservando outras sessões. Falha geral invalida a lista. Escritas só partem de edição confirmada ou botão de mute, com proteção de revisão para ignorar respostas antigas. A lista preserva o foco por chave quando possível e oferece rolagem, volume numérico e botão de mute por linha. O volume desta aba representa a sessão do aplicativo.

Wrappers verificados na tag 3.1.0 incluem AudioSessionManager.Sessions, AudioSessionControl.GetSessionInstanceIdentifier/GetProcessID/IsSystemSoundsSession/State e SimpleAudioVolume.Volume/Mute. [Manager](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioSessionManager.cs), [sessão](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioSessionControl.cs), [volume](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/SimpleAudioVolume.cs).

Dez consultas de leitura nesta máquina observaram médias de 1,40 ms para endpoint e 1,57 ms para mixer. A amostra curta mede tempo de parede. Frequências continuam moderadas. Medição de CPU, otimização e notificações de longo prazo serão avaliadas no Incremento 4.

## Implementação original do Incremento 2

`CoreAudioVolumeAccess` encapsula o endpoint e seu `AudioEndpointVolume`. Acesso master escalar e mute usam `MasterVolumeLevelScalar` e `Mute`. O formato e `Channels.Count` confirmam o mapeamento para `Channels[index].VolumeLevelScalar`. Os objetos são criados, consultados e descartados dentro de uma operação em worker MTA. `MMDevice.Dispose` descarta o wrapper de volume, que cancela seu callback nativo interno. A UI recebe `EndpointVolumeState`, sem referências COM. [NAudio 3.1.0](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioEndpointVolume.cs).

`EndpointVolumeSession` serializa as leituras e escritas de cada painel. Cada seleção possui ID fixo, revisão e token de cancelamento. Trocar a seleção invalida comandos enfileirados e respostas antigas, inclusive ao selecionar novamente o mesmo ID. Uma chamada nativa já iniciada pode terminar no endpoint original. Todos os comandos usam o ID capturado. Cancelamento é conferido antes das escritas, e uma operação de balanço parcialmente aplicada tenta restaurar os canais anteriores. O encerramento aguarda a enumeração e as filas de controle, descarta os recursos de vida útil e encerra os timers.

Sincronização externa utiliza polling de aproximadamente 600 ms por painel, com uma leitura por vez. A aplicação usa os valores lidos para atualizar os controles sob uma flag que impede gerar comandos. Escritas são originadas pelas ações de edição e botões. Os controles aguardam o retorno da operação e exibem a releitura do dispositivo. Uma revisão local adicional invalida respostas de leitura iniciadas antes de uma edição, inclusive se o retorno à UI ocorrer depois da escrita. Consultas e alterações de seleção usam somente leitura até uma ação explícita de edição.

O balanço atual é restrito a dois canais de volume e formato de mixagem com dois canais e `ChannelMask == 3`, confirmando FL/FR. Mono, multicanal e máscara ausente/desconhecida recebem explicação. A base é `max(L, R)` dos níveis de canal lidos imediatamente antes do comando. Posição positiva atenua L e negativa atenua R. Centro mantém ambos na base. A ordem de escrita começa pela atenuação quando há troca de lado. Em silêncio total, ambos permanecem em zero. O nível master usa a semântica do Windows, com leitura posterior dos canais para refletir o balanço efetivo.

Volume rejeita valores fora de 0–100 e números não finitos. Balanço rejeita valores fora de −100–100. Volume e mute têm confirmação por releitura, e o balanço confirma canais individualmente. Para acomodar quantização, a tolerância de confirmação é 0,02 no escalar. No balanço, também é rejeitada base lida superior à anterior em mais de 0,00001, admitindo apenas diferença numérica pequena. Falhas, controles ignorados pelo driver e canais acoplados geram erro e bloqueiam o controle afetado até atualização ou nova seleção. A restauração de uma atualização parcial também é conferida por leitura. Falha na restauração orienta conferir os níveis no Windows.

As duas chamadas de canal e a restauração são operações sequenciais. Desconexão física ou alteração simultânea por outro aplicativo pode impedir restaurar os valores anteriores. Esses cenários exigem teste físico por driver. As escritas foram testadas com endpoints simulados. No hardware real foram verificadas leituras e descarte. O funcionamento em modo exclusivo depende do suporte do dispositivo. [IAudioEndpointVolume](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume).

## Implementação do Incremento 1

A revisão técnica do commit a3a3358 foi aprovada pelo proprietário. A solução possui Desktop → Audio e Tests → Audio. Audio referencia somente `NAudio.Wasapi` 3.1.0, com `NAudio.Core` 3.1.0 e `System.Numerics.Tensors` 9.0.0 transitivos. Os três projetos têm lock files e destino `net10.0-windows`, x64. O destino técnico do framework permite compilar com as APIs utilizadas. A matriz de suporte do produto continua priorizando Windows 11, com Windows 10 experimental.

`CoreAudioDeviceService.QueryAsync` serializa consultas com `SemaphoreSlim` e usa `Task.Run` em worker MTA. Cada consulta cria e descarta seus próprios objetos COM na mesma thread, através de `using`. A UI recebe `DeviceCatalog`, `DeviceSnapshot` e `MixFormatSnapshot`, com valores copiados e listas somente de leitura. Como o Incremento 1 usa apenas consultas pontuais, a thread MTA dedicada e notificações persistentes descritas abaixo ficam para o incremento de hotplug.

A API NAudio 3.1.0 foi conferida no código da tag e validada por compilação e consultas reais. São usados `EnumerateAudioEndPoints`, `TryGetDefaultAudioEndpoint`, `MMDevice.ID`, `FriendlyName`, `State`, `CreateAudioClient` e `MixFormat`. A coleção e cada endpoint têm descarte próprio. `CreateAudioClient` exige descarte de cada cliente. [Enumerador](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDeviceEnumerator.cs), [coleção](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDeviceCollection.cs), [endpoint](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDevice.cs).

`DeviceSelection` guarda IDs independentes durante a execução. Inicialmente prefere o padrão multimídia. Uma preferência ausente continua registrada e é exibida como indisponível após atualizar. A seleção pode ser substituída explicitamente. Todas as chamadas Core Audio atuais são consultas. O botão Atualizar solicita nova enumeração e mantém as seleções. A consulta desabilita temporariamente os seletores e o botão, mantendo o loop de mensagens da janela disponível. Ao fechar, o token cancela a consulta entre endpoints e impede atualizar controles encerrados. Chamadas COM em andamento terminam antes do descarte local dos wrappers.

Erros por endpoint/direção geram avisos e preservam os dados obtidos dos outros dispositivos. Falha geral esvazia os controles e informa como tentar novamente. Metadados ausentes e endpoints inativos mantêm formato indisponível. O formato mostrado representa o mix do Windows. O campo de bits representa `BitsPerSample` do contêiner, com codificação exposta pelo wrapper.

## Base da primeira versão

C# com .NET 10 LTS, Windows Forms, destino `net10.0-windows` e processo x64. O SDK local é 10.0.404. Ao criar a solução, fixar essa linha em `global.json`, permitindo patches compatíveis, e configurar `AssemblyName` como `WaveStudio` para gerar `WaveStudio.exe`.

A solução `WaveStudio.slnx` começará com três projetos úteis.

| Projeto previsto | Responsabilidade |
| --- | --- |
| `src/WaveStudio.Desktop` | Janela pt-BR, apresentação, comandos, persistência JSON e composição dos serviços |
| `src/WaveStudio.Audio` | Modelos de endpoints/sessões, contratos e implementação Core Audio com NAudio, notificações e descarte |
| `tests/WaveStudio.Tests` | xUnit para regras de balanço, configurações e reconciliação de dispositivos/sessões |

Desktop referencia Audio. Audio permanece independente de WinForms. Persistência e regras de apresentação terão classes separadas e testáveis dentro de Desktop. Core, DSP e Mixer serão extraídos conforme houver implementação que justifique cada projeto.

## Controle de áudio da Alpha

Enumerar entrada e saída independentemente, incluindo ID, nome, estado e padrões de multimídia/comunicações. Exibir canais e formato de mixagem quando consultáveis. Metadados ausentes recebem indicação de indisponibilidade. A seleção na janela define o endpoint administrado pelo programa. Para alterar o padrão do sistema ou formato padrão, orientar pelas configurações nativas.

Usar as interfaces públicas `IMMDeviceEnumerator`/`IMMNotificationClient`, `IAudioEndpointVolume`, `IAudioMeterInformation`, `IAudioSessionManager2`, `IAudioSessionControl2` e `ISimpleAudioVolume`, através dos wrappers compatíveis da versão escolhida do NAudio. Conferir assinaturas no código dessa versão durante a implementação. O controle de endpoint e o controle por sessão têm alcances próprios e dependem do modo de áudio. [Documentação de volume](https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpoint-volume-controls), [sessões](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions).

Balanço exige canais L/R identificáveis e controle por canal funcional. Aplicar atenuação ao lado oposto, preservando o nível-base, com centro neutro. Mono, canais sem mapeamento e falhas de suporte deixam o controle desabilitado com motivo. Ler novamente os valores após alterações. Medidores usam picos reais expostos pela API, com indisponibilidade explícita. Medição de clipping do futuro motor exige acesso às amostras.

Sessões são identificadas pela instância em execução. PID serve como informação auxiliar, pois pode ser reutilizado. Tratar sons do sistema, múltiplas sessões por processo, processo encerrado e acesso negado. Preferências duráveis exigem identidade de aplicativo verificável e endpoint, evitando aplicar volume a outro processo. Agrupamento visual futuro preservará sessões individuais.

## Concorrência e recuperação

Um serviço de áudio com thread de trabalho MTA será responsável por COM, registros de eventos e ciclo de vida dos objetos. A UI STA recebe snapshots e atualiza controles por despacho para sua thread. Callbacks enfileiram notificações breves. Operações de volume e refresh são serializadas, com identificador de geração para descartar respostas de endpoints já substituídos.

Começar com leitura de medidores em torno de 10 Hz, ajustável após medir consumo. Coalescer hotplug e mudança de padrão. Ao remover um dispositivo, invalidar controles, cancelar tarefas, remover assinaturas e liberar referências. Manter a preferência do endpoint ausente, mostrar seu estado e permitir outra seleção explicitamente. Modo de seguir o padrão será uma preferência própria. Reenumerar sessões após criação/encerramento e após recuperação. Encerramento cancela trabalho, remove assinaturas e descarta recursos de maneira idempotente.

## Configurações e privacidade

Arquivo `%LOCALAPPDATA%/WaveStudio/settings.json`, com `schemaVersion`, seleções independentes, modo de seguir padrão, preferências por endpoint e estado da janela. Usar `System.Text.Json`. Validar intervalos e migrar versões conhecidas. Versão futura desconhecida deve gerar aviso e preservar o arquivo. Escrita serializada em arquivo temporário na mesma pasta, substituição atômica e backup da última configuração válida. Falha de escrita mantém a sessão operante e informa o usuário.

Na abertura, ler os volumes atuais do Windows. Persistir preferências de controles e valores escolhidos, com reaplicação de volumes somente por ação explícita de restaurar. Registrar e testar essa decisão de segurança. Nenhuma abertura deve aumentar volume inesperadamente. Logs locais contêm erros técnicos sem áudio ou dados sensíveis desnecessários. Processamento local e recursos de gravação somente mediante ação explícita.

## Caminho de integração global

Decisão arquitetural inicial para as Fases 2–3 — motor em espaço de usuário sobre WASAPI compartilhado, com endpoints virtuais fornecidos por integração documentada e assinada. A escolha do fornecedor depende de licença, disponibilidade e prova física. Essa dependência segue pendente. A Alpha utiliza os endpoints já existentes.

| Alternativa | Viabilidade e decisão |
| --- | --- |
| Core Audio de controle | Adequado para a Alpha, administra volume, sessões e medição |
| WASAPI shared | Base do motor futuro, usa formatos negociados e buffers, demanda medição de latência |
| Loopback de endpoint/processo | Útil para captura e análise, exige rota complementar para substituir o sinal ouvido |
| Endpoints virtuais | Caminho preferido para separar fluxos e entregar microfone processado a outros apps, depende de driver assinado e licença |
| APO | Alternativa para efeitos no pipeline do sistema, exige desenvolvimento nativo e integração/registro por endpoint, avaliar conflitos e modos de processamento |
| Driver próprio derivado de SysVAD | Alternativa de longo prazo, exige engenharia de driver, assinatura, compatibilidade e manutenção |
| WASAPI exclusive/ASIO | Casos avançados com acesso direto e possíveis desvios do processamento, suporte futuro condicionado a testes |

Loopback captura o fluxo reproduzido pelo endpoint. O pipeline externo precisa de entrada, processamento e entrega reais. [Loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording). APOs integram processamento ao sistema e requerem uma estratégia específica de instalação. SysVAD é uma base de estudo para driver. [APOs](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/implementing-audio-processing-objects), [SysVAD](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/).

Prova mínima de reprodução prevista — navegador → endpoint virtual de render → captura pelo motor → DSP de ganho mensurável → saída física. Prova de captura — microfone físico → motor/DSP → lado render de um cabo virtual → endpoint de captura correspondente → OBS/Discord selecionando esse endpoint. Medir sinal original e processado, duplicação, loops, latência e recuperação. Primeiro usar um par virtual por fluxo em teste. Game/Chat/Music/Media/System/AUX e saídas Virtual Microphone/Stream Mix/Recording Mix serão acrescentados conforme exigência de isolamento.

Antes de implementar muitos efeitos, registrar resultado reproduzível das duas provas ou o bloqueio específico. A solução comercial definitiva permanece condicionada a essas evidências. Instalações futuras requerem autorização. Drivers devem operar com Secure Boot, assinatura e integridade de memória habilitados, com desinstalação e recuperação verificadas.

## Evolução do motor

Na Fase 2, negociar formatos e converter para PCM float32, usando 48 kHz inicialmente quando compatível. Separar captura, DSP, barramentos e saída da UI. Definir efeitos com inicialização, processamento, reset, bypass, parâmetros, latência e descarte. Buffers pré-alocados, parâmetros suavizados e troca segura de estado. Callback sem disco, rede, alocação recorrente ou espera prolongada. Medir underruns/overruns, drift entre relógios, resampling, NaN/Infinity, CPU, RAM e latência real. Efeitos e monitoramento iniciam desligados, ganhos neutros e destinos externos dependem de configuração explícita.
