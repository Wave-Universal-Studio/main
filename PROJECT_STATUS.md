# Status do Wave Studio

Atualizado em 24/11/2026.

## Em revisão — F5-I2B-NATIVE-PCM-PACKET-ADAPTER, 24/11/2026

Adapters de captura/render transformam pacotes WASAPI PCM16/float32 já adquiridos em blocos float32 do `BoundedBusPcmTransport` e de volta em bytes de saída. Validam formatos, canais e 44,1/48 kHz, frames completos, endpoint/generation, SILENT e flags de falha; não usam COM, driver nem criam dispositivos. A saída verifica o ticket de geração antes de disponibilizar bytes. O owner nativo futuro permanece responsável por clock, liberação de buffers e entrega real.

Oito casos novos simulam pacotes reais e verificam conversão, clipping, privacidade, silêncio e invalidade; GitHub Actions Windows x64 ainda vai validar a PR. **Ainda não há serviço contínuo WASAPI, áudio OBS/Discord ou captura separada por aplicativo.**

## Implementado — F5-I2A-BOUNDED-PCM-BRIDGE, 24/11/2026

Nova ponte `BoundedBusPcmTransport` liga o núcleo PCM de F5-I1 a produtores de blocos de captura e consumidores independentes de Personal/Stream. As filas SPSC são limitadas por fonte e barramento, identificam geração, formato e bloco de 10 ms, recusam dados antigos/incompletos e só retornam saídas com ticket ainda válido. A saída externa permanece opt-in e não abre nenhum dispositivo. Se o consumidor não acompanhar, a entrega falha fechada, sem substituir áudio ainda não consumido.

São seis novos testes de integração em memória, cobrindo isolamento, privacidade, fila cheia, identidade, gerações obsoletas e fechamento. **Ainda não há conexão nativa contínua, clock WASAPI real, reprodução física, OBS/Discord nem nova interface de usuário.** A [PR #3](https://github.com/Wave-Universal-Studio/main/pull/3) foi integrada por squash no commit `c7eb383b7293424ea9430455a6e56f8da574f44e`. [CI Windows x64 final](https://github.com/Wave-Universal-Studio/main/actions/runs/45336327862): **938 testes comuns aprovados em Debug e 938 em Release**, zero falhas, sete Explicit excluídos por configuração e builds sem avisos/erros. A ponte permanece interna; sem WASAPI contínuo nem OBS/Discord conectados. [Issue #2](https://github.com/Wave-Universal-Studio/main/issues/2).

## Implementado — F5-I1-BUS-MATRIX-CORE, 24/11/2026 (PR #1 integrada)

O motor interno soma PCM float32 em **Personal Mix** e **Stream Mix** por uma matriz de envios explícitos. Cada envio e master tem ganho e mute independentes. O transporte injetado exercita conversão, soma, proteção de saída, medição, isolamento, desconexão e renovação de geração usando amostras. A saída externa é opt-in, com autorização específica para conteúdo sensível e proteção contra retorno para a mesma chamada.

**Validação final integrada:** Linux restaurou pacotes em modo locked/auditoria, compilou Windows x64 e executou 18 casos portáteis de PCM por configuração. Em [GitHub Actions Windows x64](https://github.com/Wave-Universal-Studio/main/actions/runs/45333419531), os builds Debug e Release terminaram sem avisos ou erros; cada regressão registrou **932 aprovados, zero falhas e sete Explicit excluídos** (939 totais). A correção de um teste legado de clock simulado esperou o tempo monotônico da leitura, sem modificar o backend de áudio. PR #1 integrada por squash no commit `787df9181f62bc5b340300ee53d1b0ecabf83eca`. [Plano de testes](docs/TEST_PLAN.md#f5-i1-bus-matrix-core)

**Próxima etapa F5-I2** preparada na [Issue #2](https://github.com/Wave-Universal-Studio/main/issues/2): integração de fluxos e destinos reais após análise de relógios, isolamento, disponibilidade do Windows e segurança. A F5-I1 opera exclusivamente com transporte PCM injetado e não ativa endpoints nem adiciona controles à interface. Não repetir testes físicos já aprovados, reinicializações ou instalar drivers sem necessidade e autorização específica.

## Histórico — F4-I2-GAMECHAT-SOLO-LANG, 24/11/2026

A Mesa de som ajusta o balanço das sessões ativas que o usuário atribui a **Jogo** e **Comunicação** na saída selecionada. O gesto atenua o grupo oposto até zero em relação ao volume original de cada instância. **Centralizar**, mudar os participantes, trocar de saída, sair da mesa ou fechar normalmente restauram apenas os níveis que ainda correspondem à atuação do aplicativo. Conflitos e restauração sem confirmação são sinalizados. O controle atua nas sessões do Windows e pode afetar a transmissão.

Solo fica fora da entrega. O mute aplicado a outras sessões pode atingir chamadas, e a API de sessões oferece operações de leitura e escrita separadas. A restauração seria condicional e ficaria exposta à concorrência e ao término inesperado do processo. O estudo de solo em monitoramento próprio permanece no planejamento posterior.

Configurações → Idioma oferece pt-BR, en-US, es-ES e Seguir Windows. A escolha persiste no esquema 3 e passa a valer após reabrir o aplicativo. O idioma de exibição acompanha a escolha. A formatação numérica mantém a cultura regional do Windows.

Restore locked com auditoria NuGet, builds x64 não incrementais sem avisos ou erros e regressões finais com 914 aprovados em Debug e 914 em Release, zero falhas e sete Explicit excluídos por configuração. A validação desta etapa foi automatizada.

A aprovação dos dois fluxos externos continua registrada como relato do proprietário. Estabilidade prolongada, recuperação em hardware, latência acústica, necessidade de drift e aceitação instrumentada dos fluxos externos seguem como pendências de release. [Uso da mesa e idioma](README.md#mesa-de-som-e-idioma) · [Validações](docs/TEST_PLAN.md#f4-i2-gamechat-solo-lang--balanço-e-idiomas).

## Histórico — F4-I1-REAL-MIXER, 23/11/2026

A aba **Mesa de som** mostra as sessões reais da saída escolhida em **Dispositivos**. Cada sessão tem faixa própria, fader vertical de 0–100%, mute e pico instantâneo quando a leitura está disponível e atual. O master controla o volume e o mute do dispositivo no Windows e mostra o pico agregado dessa saída. A grade existente continua acessível em **Detalhes das sessões**. É possível atribuir grupos a canais presentes e filtrar a mesa durante a sessão.

A alternância de dispositivo e o fechamento cancelam gestos pendentes. A atualização periódica preserva o fader em edição. A escrita ocorre ao soltar o fader ou após pausa breve no teclado e confirma a identidade e o valor observado. Sessões ambíguas, encerradas ou removidas deixam de oferecer comandos válidos. O motor DSP, o teste de microfone e o fluxo de prontidão externa foram preservados.

**Configurações → Idioma** oferece Português (Brasil) e Seguir Windows. Quando o idioma do sistema ainda não está disponível, a interface usa pt-BR. A preferência é gravada com as demais configurações no esquema 3, com leitura compatível dos esquemas 1 e 2 e as proteções de armazenamento existentes. A formatação numérica acompanha o Windows.

O proprietário confirmou manualmente os dois fluxos externos. O relato faz parte do histórico e segue sem telemetria de um runner associada. Estabilidade prolongada, recuperação, latência e drift em hardware permanecem como pendências técnicas de release. A entrega atual permanece na Fase 4. [Uso da mesa e idioma](README.md#mesa-de-som-e-idioma) · [Validações](docs/TEST_PLAN.md#f4-i1-real-mixer--mesa-e-idioma).

## Histórico — F3-I3-ROUTE-READINESS-DESKTOP, 19/11/2026

A janela atual oferece **Roteamento externo (experimental)**. A aba mostra o inventário de reprodução e captura, o formato informado, as escolhas de sessão, o pareamento, a saúde PnP, os padrões do Windows e a compatibilidade de cada fluxo. O cabo simples prevê dois fluxos sequenciais com restauração e quietude entre eles. A+B fica disponível para seleção quando o inventário confirma dois pares ativos independentes.

A consulta local somente leitura encontrou dois pinos de reprodução e um de captura do VB-CABLE simples, com Código 10 em outra instância sem pino ativo associado. Os fluxos selecionados de navegador e microfone passaram na pré-verificação de compatibilidade. A+B ficou como não detectado neste inventário. O diagnóstico técnico fica distinto da prova física pendente. Nenhuma preferência de rota externa é gravada.

Os 16 casos direcionados e as regressões x64 passaram, com 867 aprovados por configuração e sete Explicit excluídos. Os builds Debug e Release terminaram com zero avisos ou erros. [Evidência automatizada](docs/TEST_PLAN.md#f3-i3-route-readiness-desktop--compatibilidade-em-leitura).

A aba oferece instruções textuais de configuração manual e restauração. Em `a109426`, o proprietário relatou o funcionamento dos dois fluxos. Essa aceitação manual foi registrada sem métricas associadas. A prova instrumentada continua pendente, e Config → Idioma segue previsto para a primeira etapa de interface pós-F3. [Uso e limites da aba](docs/EXTERNAL_ROUTE_PROOF.md#f3-i3-route-readiness-desktop--prontidão-na-janela).

## Histórico — F3-I2-POSTBOOT-PAIRING, 19/11/2026

**Pareamento corrigido e conferido em leitura. Prova física pendente.** A `main` avançou de `0a4ccf5` para `ca7fd1b` por fast-forward. O boot completo mais recente registrado antecede a instalação do VB-CABLE. Depois da instalação, houve desligamento e Inicialização Rápida. O proprietário informou que prefere adiar novas reinicializações. Padrões e níveis de áudio foram conferidos com o snapshot original.

O código 10 (`STATUS_DEVICE_BUSY`) persiste numa segunda instância PnP, sem endpoints ativos associados. Os três endpoints do cabo escolhido estão ligados à instância saudável. O pino de captura retorna um ID de topologia diferente dos de reprodução mesmo no mesmo devnode. O caminho de cabo único agora resolve a instância PnP pelo dispositivo conectado, exige driver esperado e estado iniciado sem erro, e reúne todos os pinos ativos dessa instância. A pré-verificação Release, somente leitura, confirmou os quatro dispositivos escolhidos, dois pinos de reprodução, um de captura do cabo e os seis padrões fora do cabo.

A única prova agrupada continua sujeita ao boot completo registrado, às demais pré-condições e a autorização específica. A aceitação física das Fases 2 e 3 segue em aberto. [Diagnóstico e verificações](docs/EXTERNAL_ROUTE_PROOF.md#f3-i2-postboot-pairing--diagnóstico-e-correção).

## Histórico — F3-I2-SINGLE-CABLE-PROOF, 19/11/2026

**Bloqueada antes de abrir qualquer fluxo.** A `main` avançou por fast-forward de `ca6d7da` para `22811d1`. A base funcional, os hashes dos binários Release x64, o filtro Explicit único e o WAV sintético foram conferidos. As evidências automatizadas anteriores foram reaproveitadas.

Com autorização de instalação, o VB-CABLE simples Pack46 foi instalado com driver assinado 3.5.2.0. O OBS 33.0.1 ficou preparado em modo portátil isolado. Os seis dispositivos padrão por função/direção e os níveis, mute e balanço dos oito endpoints anteriores coincidem com o snapshot. O log registra inicialização do tipo 1 após o desligamento, com o último boot completo anterior à instalação. Uma das duas instâncias PnP do cabo registrou código 10, e os três endpoints ativos pertencem à instância saudável.

A pré-verificação do Wave Studio rejeitou a identidade de adaptador dos quatro endpoints selecionados. Navegador, sondagem de quietude e microfone/OBS ficaram pendentes. Nenhuma preferência dos aplicativos foi aplicada. A autorização física ainda não foi solicitada e nenhuma tentativa foi consumida. A revisão retoma o bloqueio de pareamento antes de agendar uma única prova agrupada. As pendências integradas da Fase 2 e a aceitação da Fase 3 seguem abertas. [Evidências da preparação e do bloqueio](docs/EXTERNAL_ROUTE_PROOF.md#f3-i2-single-cable-proof--bloqueio-antes-do-áudio).

## Histórico — F3-I2-SINGLE-CABLE, 19/11/2026

O proprietário escolheu o VB-CABLE simples para avaliação local, sem adquirir A+B neste momento. A `main` sincronizou por fast-forward de `48b5045` a `422c7fe`. As mudanças posteriores à base funcional `6c7a5d4` eram documentais até esta adaptação. A conectividade do Git foi conferida com Git 2.56.1, SDK .NET 10.0.404 e política Restricted. O estímulo estéreo de 1 kHz foi reaproveitado e seu SHA-256 conferido.

O modo de quatro endpoints confirma o par virtual e sua separação dos endpoints físicos. Usa o motor FIR/SPSC/PCM dos dois cabos, com até cinco segundos por rota e ganho máximo 0,05. O navegador usa o cabo primeiro, com OBS na seleção original. Um único coordenador mantém o guard do lote enquanto cada fluxo usa sua própria geração, ticket e recursos. A restauração do navegador e o fechamento seguro são requisitos antes da sondagem. A segunda fase depende de ausência observada de produtores nos pinos ativos do cabo, captura silenciosa contínua e fresca dentro de 3 s, nova seleção conferida e novo consentimento. O método de quietude falha sem dados suficientes e conserva o guard em caso de Stop/Release incertos.

Os testes exercitam os destinos simulados, a relação de amplitudes, restauração, cancelamento, transição bloqueada por produtor ou sinal residual, falhas de captura, fechamento e perda de dados. A configuração A+B conserva seis IDs, dois adaptadores distintos e seu Explicit. A nova prova agrupada tem método independente e roteiro único com snapshot local e revisão de dependências. [Prova com um cabo](docs/EXTERNAL_ROUTE_PROOF.md#f3-i2-single-cable--avaliação-com-um-cabo).

### Verificações

| Regressão padrão x64 | Seed | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug | 20261114 | 839 | 0 | 7 | 87,140 s |
| Release | 20261113 | 839 | 0 | 7 | 50,981 s |

Restore locked com auditoria de todos os pacotes e NU1900 como erro. Builds não incrementais Debug e Release x64 com zero erros ou avisos. Os 33 direcionados em Release (20 novos e 13 da rota A+B) passaram em 10,990 s. Os 20 novos casos elevam a referência de 819 para 839 comuns. A descoberta pelo filtro exato encontrou um novo Explicit, sem executar a prova. O teste isolado de inspeção digital mediu zero bytes recorrentes em mil verificações de cada formato, com controle positivo do contador. O caso de perda de pacotes no fim da janela confirmou a exigência de frescor terminal.

O primeiro build com os testes novos acusou ausência das APIs de cabo único. Após a implementação, o Debug direcionado passou 14 casos em 5,380 s. Uma rodada integrada intermediária aprovou 30 direcionados e 836 comuns por configuração. A revisão final antecipou o vigia de produtores para antes da sondagem, restringiu a exceção do próprio processo ao pino render escolhido e acrescentou três casos adversariais. Os dois builds e as duas regressões foram reexecutados nessa revisão. Nenhuma prova anterior foi repetida.

Comandos finais: `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`, builds Debug/Release com `--no-restore --no-incremental -warnaserror` e regressões com `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`. Logs e hashes dos binários Release em `artifacts/f3-single-cable`, ignorados pelo Git. A integridade de armazenamento e áudio em hardware ainda depende da execução autorizada.

O pacote oficial candidato Pack46 ainda exige verificação concreta e autorização de instalação/elevação, com snapshot de padrões/níveis e reinício conduzido pelo proprietário. A configuração de aplicativos e o áudio exigem autorização específica posterior. Os fluxos reais com navegador e OBS ainda não foram exercitados em hardware. Estabilidade prolongada, recuperação física, latência acústica e necessidade de drift permanecem sob validação integrada. Nenhuma instalação, compra, alteração Windows, correção física ou prova foi realizada e a F4 permanece fora do escopo.

## Histórico — F3-I2-EXTERNAL-ACCEPTANCE, 19/11/2026

**Preparação verificada. Aceitação física aguarda escolha de dependência.** A `main` foi sincronizada por fast-forward de `6c7a5d4` até `fdd2816`. A diferença é exclusivamente documental. A conectividade Git, o SDK 10.0.404, o Git 2.56.1 e a política Restricted foram conferidos.

O inventário somente leitura encontrou seis endpoints PnP presentes com estado OK no Windows 10 Pro x64, build 19045. A busca não identificou VB-CABLE A+B ou VoiceMeeter entre endpoints e registros de drivers, nem pacote oficial A+B ou OBS nas pastas e registros consultados. Essa busca não exclui cópias portáteis em outros locais. O estado consultado indicou Secure Boot ativo e serviço de integridade de memória fora de execução. Nenhuma configuração foi modificada. Versão, assinatura e compatibilidade efetiva dos drivers permanecem sem verificação local até que haja pacote.

O Release x64 foi reaproveitado. O metadado de versão referencia `31acd2f`, base de planejamento sobre a qual se compilou a implementação antes de publicá-la em `6c7a5d4`. Os três PDBs correspondem às respectivas DLLs e os checksums de 131 fontes C# coincidem com a árvore atual. O apphost e as DLLs são AMD64. Os oito documentos virtuais de fontes geradas sem arquivo local ficam fora da comparação de bytes. Os hashes dos binários e os detalhes estão no log ignorado `artifacts/f3-external-acceptance/provenance-final.log`. A evidência anterior de 819 casos por configuração e da descoberta do método único foi preservada, sem repetir build, restore, regressões ou descoberta do runner.

A loja oficial exibe US$ 15 para A+B na contribuição padrão e libera o pacote após pagamento pelo proprietário. O candidato OBS é o ZIP x64 oficial 33.0.1 em modo portátil. A rota sem pagamento antecipado usa como ponto de partida o VB-CABLE simples, com adaptação separada e revisão antes de prova. A escolha fica com o proprietário. A autorização de instalação ou elevação exige o pacote concreto e sua assinatura verificada. A autorização de configuração temporária, captura e reprodução fica para o preflight da sessão agrupada. [Dependências, verificação e roteiro](docs/EXTERNAL_ROUTE_PROOF.md#f3-i2--preparação-da-aceitação-agrupada).

Foi gerado localmente um estímulo WAV sintético, sem reprodução. As duas fases seguem planejadas para a mesma sessão autorizada, com snapshot privado independente do runner, reconfirmação de configuração antes de cada fluxo e restauração verificada depois da quiescência. As evidências físicas anteriores e as pendências de fechamento da Fase 2 foram preservadas.

## Histórico — F3-I1-EXTERNAL-ROUTE, 19/11/2026

O proprietário autorizou expressamente iniciar o desenvolvimento da Fase 3 e acompanhar as pendências F2 em validação integrada. A `main` foi sincronizada de `d743351` para `31acd2f` por fast-forward seguro, com base funcional inicial `0c1e54b`, Git 2.56.1, .NET SDK 10.0.404 e política Restricted. A prova 4, nominal SILENT aprovada, DIAG-PROOF reprovada, sete manuais e aprovação de escuta sem telemetria foram preservados.

Foi implementada a rota nominal de prova em dois fluxos sequenciais. Na primeira fase, o navegador é direcionado ao cabo A e o motor entrega PCM processado ao headset. Na segunda, o microfone físico alimenta o cabo B e sua captura fica disponível para uma fonte no OBS. Em fakes, os mesmos donos WASAPI, FIR, FIFO e encoder entregaram PCM e demonstraram relação de amplitude aproximada de 0,25 entre efeito e bypass. O teto de segurança é 0,05, com entrega após corte coerente e até 5 s por fluxo. Rejeições de topologia, perda de sinal, parada, mudança de seleção, falhas de endpoint e limpeza incerta impedem progressão indevida. O contrato de restauração compara o snapshot original e as seleções temporárias e atuais, com retorno manual e detecção de conflito.

A escolha para prova é VB-CABLE A+B e OBS local. O inventário local de Windows 10 19045 mostrou seis endpoints presentes e nenhum candidato A+B detectado. A instalação do pacote oficial, a verificação de assinatura/HVCI e o OBS disponível precisam de avaliação e autorização própria antes da execução. A dependência é candidata para a prova, sem redistribuição pelo projeto. [Contratos, comparação e roteiro agrupado](docs/EXTERNAL_ROUTE_PROOF.md).

### Verificações

| Regressão padrão x64 | Seed | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug | 20261114 | 819 | 0 | 6 | 91,996 s |
| Release | 20261113 | 819 | 0 | 6 | 54,693 s |

Restore locked com auditoria de todos os pacotes e NU1900 como erro. Builds Debug/Release x64 não incrementais com zero erros ou avisos. O teste direcionado aprovou 13 casos em Release em 13,156 s. São 806 casos comuns anteriores e 13 novos. A descoberta exata encontrou um novo Explicit agrupado e o excluiu das regressões, junto com os cinco anteriores. A medição isolada cobriu mil codificações externas após aquecimento com zero bytes recorrentes e controle positivo de alocação.

O primeiro build direcionado acusou APIs novas ainda ausentes e, após implementação, duas referências de fixture incompatíveis com o fake existente. O primeiro teste dirigido executado aprovou cinco de nove casos. Quatro cenários usavam a sentinela de render constante do fake SILENT e falharam antes de liberar o buffer. O callback dos novos casos passou a verificar a entrega da rota externa, preservando a sentinela dos testes SILENT. O primeiro probe nativo sem Start encontrou a propriedade de instância PnP ausente nos seis endpoints. A consulta pública à topologia conectada recuperou seis IDs em quatro grupos de adaptadores e registrou formatos sem nomes ou IDs no relatório. As regressões finais foram executadas após essa correção de identidade no preflight. Os pareamentos A+B estão pendentes de confirmação no hardware candidato.

Comandos: `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`; builds não incrementais Debug/Release com `--no-restore -warnaserror`; testes com `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`. Logs e probe somente leitura em `artifacts/f3-external-route`, ignorados pelo Git. Nenhum Start físico, instalação ou mudança de configuração Windows foi executado.

A próxima decisão é a revisão desta entrega seguida de autorização específica para dependências e validação física agrupada. A Fase 2 segue aberta para estabilidade prolongada, comportamento real de parada/recuperação, latência acústica e necessidade de drift. A evidência de integração real de navegador e OBS também está pendente. A fase atual prepara essa prova sem monitoramento contínuo de produção ou correção física.

## Histórico — F2-I6D-AUDIBLE-PROOF, 19/11/2026

**Aprovação manual de escuta registrada.** Em 19/11/2026, o proprietário confirmou que ouviu a própria voz pelo headset e que o monitoramento funcionou. O relato cobre a escuta e o funcionamento percebido. Nível durante a reprodução, timbre, atraso percebido, feedback e eventual uso de Esc durante a sessão não foram informados.

A `main` foi sincronizada por fast-forward de `95f4ffd` a `fa0e5f2`. A conectividade Git e a equivalência com a base funcional `0c1e54b` foram conferidas. O apphost e as DLLs Release x64 conservam a revisão `e14ff84` e os hashes verificados no preparo. Os registros do restore auditado, do build sem erros ou avisos e da descoberta única foram recuperados dos artefatos locais existentes.

**Resultado automatizado indisponível.** O estado `AguardandoOperador` pertence à janela que fechou antes de lançar o apphost. A busca pelos artefatos da execução associada ao relato não localizou JSON, log de execução ou código de saída. A atribuição da escuta a um runner específico, a duração, o ganho real, o PCM entregue, a continuidade, a FIFO e o encerramento ficam sem verificação automatizada. A prova nominal SILENT e as aprovações dos sete casos manuais permanecem evidências anteriores. Nenhuma prova, regressão, descoberta de teste ou build foi repetido nesta retomada. [Evidências e limites](docs/WASAPI_BACKEND_PLAN.md#f2-i6d-audible-proof--aprovação-manual-e-evidência-disponível).

### Decisão para iniciar a Fase 3

1. Revisar o aceite de escuta e a lacuna de telemetria. O proprietário deve decidir expressamente se as pendências da Fase 2 precisam ser fechadas antes da integração externa ou se alguma pode permanecer aberta para validação agrupada. O registro atual não encerra a Fase 2.
2. Para fechar as pendências do motor, confirmar estabilidade do monitoramento contínuo, integridade da fila e parada, recuperação após falha ou desconexão e latência acústica medida separadamente da fila e do algoritmo. Determinar com dados físicos se o caminho nominal se sustenta no uso pretendido e se exige correção de drift. Qualquer ativação de correção física depende de validação própria. Reaproveitar a gravação curta e os aceites existentes.
3. Com a transição autorizada, delimitar uma prova de integração da Fase 3 que demonstre DSP perceptível em navegador e microfone processado disponível em OBS ou Discord, por rota suportada e reversível, com prevenção de loops, parada e restauração verificáveis. Agrupar as validações manuais indispensáveis em uma entrega funcional. [Escopo das fases](docs/REQUIREMENTS.md#11-entregas-e-ordem-de-implementação).

## Histórico — F2-I6D-ALLOC-MONITOR, 18/11/2026

A `main` sincronizou por fast-forward de `a85af9d` a `dd9b227`, com conectividade Git conferida. Ambiente .NET SDK 10.0.404, CoreCLR 10.0.15 x64, Git 2.56.1 e política Restricted. A prova nominal SILENT aprovada, a prova 4, a coleta diagnóstica reprovada e os sete testes manuais conservam suas evidências originais.

### Diagnóstico da contagem de alocações

| Caso | Bytes registrados no histórico | Janela na thread do teste |
| --- | --- | --- |
| `WasapiCheckpointTests.PublicationUsesBoundedPreallocatedMemoryAndNoRecurringAllocations` | 7.912 e 6.856 no Release padrão, seeds 20261113 e 20261114 | 100 mil pares após 20 mil de aquecimento, com limite separado de setup |
| `EqualizerPreviewTests.ProcessedReadAllocatesNothingAndInvalidationSuppressesCopiedPacket` | 2.632 e 5.640 no Release seed 20261113. O histórico anterior tem 6.040 em Debug | 100 leituras processadas após 100 de aquecimento |
| `NominalPartialConsumptionTests.SmallOutputPreservesSamplesButRepeatsChannelMappingWork` | 3.992 no Release seed 20261113 | Processamento e flush de 8.192 frames, medidos separadamente com saídas de 512 e 1 frame após aquecimento |
| `AdaptiveResamplingQualityTests.PartialOutputHasLinearMappingWorkNoRecurringAllocationsAndEquivalentAudio` | 3.992 no Release seed 20261113 | Processamento e flush de 8.192 frames, com saídas de 512 e 1 frame e ajuste do passo ativo |

Antes da correção, três execuções direcionadas dos mesmos 125 testes passaram sem reproduzir os bytes. Foram duas em Release padrão com seeds 20261113 e 20261114 e uma serial com seed 20261113. As reprovações anteriores de suítes completas permanecem no histórico abaixo. O contador é da thread da janela. O probe colocou a carga em outra thread e registrou o índice de coletas concluídas para verificar se bytes poderiam aparecer mesmo sem instruções de alocação.

O probe .NET x64 independente, sem xUnit e com configuração padrão de GC/JIT, pré-alocou as amostras e aqueceu as chamadas. Na thread medida, um objeto mantido vivo antes da leitura abre o contexto de alocação. Dentro da janela negativa há apenas duas leituras de `GC.GetAllocatedBytesForCurrentThread()` e `Thread.SpinWait(240_000)`. O IL conferido não contém instrução de criação de objeto ou boxing. A outra thread rotacionou blocos de 2 KiB em um conjunto de 4 MiB e solicitou `GC.Collect(2, Forced, blocking: false, compacting: false)`. Houve **32/32 janelas vazias em zero** sem carga, **32/32 controles positivos em 64 bytes** com `new byte[37]` e **7.560 bytes em uma de 192 janelas vazias** sob carga. Essa janela coincidiu com a conclusão de um GC em segundo plano. O primeiro probe também mediu a supercontagem e falhou depois ao procurar o método para imprimir o IL. A execução com a consulta do IL corrigida terminou com código 0 e registrou a correlação.

O CoreCLR 10.0.15 calcula o contador a partir dos bytes reservados menos a sobra do contexto corrente. Ao retirar o contexto durante GC em segundo plano, o runtime podia zerar os ponteiros sem debitar essa sobra. O mecanismo reproduz a classe de falso positivo observada, inclusive na ausência do processamento WAV/DSP. A correção aceita no upstream debita o espaço não utilizado. A coleta de stacks de alocação exata ficou indisponível no ambiente. Não houve instalação de profiler. A evidência experimental não identifica o tipo, stack ou instante de cada valor histórico. [Implementação no runtime instalado](https://github.com/dotnet/runtime/blob/v10.0.15/src/coreclr/vm/comutilnative.cpp#L3539-L3553), [relato original](https://github.com/dotnet/runtime/issues/134724), [correção aceita pelo CoreCLR](https://github.com/dotnet/runtime/pull/134855).

A solução no Wave Studio isola somente as medições exatas afetadas, com os mesmos binários e a mesma carga, em subprocessos de teste com filtro por método. Mantém setup, aquecimento, trabalho, limites, asserções de zero, GC/JIT e paralelismo padrão da suíte principal. Cada medição inclui controle positivo e negativo fora da janela. O teste de pressão mantém GC ativo em outro processo enquanto o ensaio do diário mede sua janela original. A correção é de medição e não exige alterar o processamento medido. O controle testa o contador e o isolamento nesta configuração. A aprovação das suítes cobre as janelas verificadas, sem implicar ausência universal de alocações.

### Prova audível preparada para revisão

A fachada interna dedicada exige consentimento de uso único para a rota exata, headset, volume baixo e parada do operador. O ganho começa em zero e sobe até 0,05 somente após prefill e corte coerente. O limite é de cinco segundos em razão nominal. O método Explicit dedicado foi descoberto sem execução. Fakes com Float32 e PCM16 exercitaram encoder, fila, warmup, corte, contadores e cancelamento. As fachadas SILENT mantêm o caminho sem encoder. PCM só é contabilizado após ReleaseBuffer com S_OK. O relatório registra sinal, ganho, amplitude e clipping, sem voz, PCM ou identificação dos dispositivos. A reprodução física, nível percebido, timbre, feedback e latência acústica aguardam revisão e autorização específica. [Roteiro da prova](docs/WASAPI_BACKEND_PLAN.md#f2-i6d-alloc-monitor--roteiro-para-revisão).

### Validações

| Regressão comum, paralelismo padrão | Seed | Aprovados | Falhas | Explicit excluídos | Tempo |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug x64 | 20261114 | 806 | 0 | 5 | 87,102 s |
| Release x64 | 20261113 | 806 | 0 | 5 | 51,694 s |
| Release x64 | 20261114 | 806 | 0 | 5 | 50,599 s |

Cada execução terminou com código 0. São 801 anteriores, um caso de pressão para o isolamento, um de consentimento, dois de entrega Float32/PCM16 e um de cancelamento audível. Os direcionados passaram com cinco casos de alocação em Release (4,688 s) e 20 de monitoramento em Debug (7,087 s). O primeiro filtro combinado da medição selecionou zero casos e foi corrigido para filtro exato. Em seguida os quatro casos medidos passaram, mas a fixture de pressão não confirmou conclusão de BGC. Aumentar o conjunto retido apenas na fixture de pressão permitiu exigir a conclusão de uma coleta e repetir a medição original sob essa carga.

Restore locked com auditoria de todos os pacotes e NU1900 tratado como erro. Builds não incrementais Debug/Release pela configuração da solução, com `PlatformTarget=x64`, zero erros e avisos. A tentativa inicial com `Platform=x64` foi rejeitada pela solução, e o build preliminar apontou a asserção proposital de token não cancelável. A anotação local da asserção resolveu o aviso sem mudar a condição testada. A descoberta com `--list-tests text --filter-method` encontrou somente o novo Explicit audível. Os testes usaram `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`, sem alterar a política Restricted.

Logs, probe independente e diagnósticos anteriores e finais estão em `artifacts/f2-i6d-alloc`, ignorados pelo Git. A Fase 2 aguarda revisão desta entrega e decisão sobre a primeira prova audível. Nenhuma prova física foi executada nesta etapa.

## Histórico — F2-I6C-NOMINAL-PROOF, 18/11/2026

A tentativa física única **foi aprovada para transporte nominal limitado** no par selecionado, a 48 kHz Float32 de captura mono e render LR. O temporizador de 5 s concluiu. A janela QPC foi de **4,9991228 s**, 0,8772 ms abaixo da duração configurada. O runner Release x64 executou somente o teste dedicado, com código 0 em 5,998 s. `DurationReached`, `NominalObserved`, `RenderSilent` e `PacketRateConfirmed` ficaram verdadeiros. `DriftPrecisionAvailable`, `DriftControlEnabled` e `Cleanup.TransportObserved` ficaram falsos.

Os dois streams confirmaram Start, Get/ReleaseBuffer e Stop, todos com S_OK nos resultados reportados. Houve liberação confirmada, nenhum buffer em voo, nenhum recurso retido e nenhuma voz armazenada. Foram aceitos 499 pacotes contíguos e 239.520 frames após o descarte inicial de um pacote de 480 frames. A FIFO escreveu 239.520 e leu 237.120 frames, com saldo contábil de 2.400 e zero drops ou underruns. O render liberou 241.056 frames SILENT, soma de 1.056 de priming do endpoint, zero pré-ticket, 2.880 de warmup e 237.120 de leitura FIFO.

O diário confirmou 191 cortes, com ocupação de 0–2.400 frames e média intervalar de 2.091,054–2.109,499 frames em 4,9724887 s cobertos. O histórico de captura registrou 19 leituras provisórias e uma indisponibilidade da evidência de clock na leitura 27, por `WindowRateOutsideTolerance`. A evidência voltou a ficar disponível na leitura 28, 99,9995 ms de QPC depois, dentro do orçamento de 500 ms. A continuidade e as janelas nominais dos pacotes foram confirmadas. [Histórico, contabilidade e reprodução dos cálculos](docs/WASAPI_BACKEND_PLAN.md#f2-i6c-nominal-proof--resultado-físico-18112026).

A `main` avançou por fast-forward de `287b05d` para `134f35e`. A base funcional é idêntica, e o Release x64 foi recompilado de `134f35e` com SDK 10.0.404, restore locked auditado e zero erros ou avisos. O proprietário reconfirmou as condições imediatas e os sete manuais. O Windows mostrou um único endpoint ativo em cada direção para o par confirmado. O runner verificou a seleção exata, sem fallback. A política Restricted foi preservada. O JSON e os logs originais permanecem em `artifacts/wasapi-nominal-silent`, ignorados pelo Git.

A prova valida somente o transporte nominal limitado neste par e formato. As falhas de alocação Release ainda impedem liberar monitoramento físico audível ou contínuo. Código, provas anteriores e evidências das regressões foram preservados. A investigação de alocações e os próximos passos da Fase 2 aguardam revisão. F3 permanece pendente.

## Histórico — F2-I6C-NOMINAL-MONITOR, 18/11/2026

A `main` avançou por fast-forward de `59cc56e` para `00fc42f`; a diferença desde a base funcional `558a431` era documental. Pasta oficial, referências local/remota, SDK .NET 10.0.404, Git 2.56.1 e conectividade de objetos verificados. O SHA-256 do JSON local coincide com o publicado. A prova 4 e a tentativa diagnóstica reprovada permanecem registradas.

A rota nominal opt-in compartilha os owners MTA, o guard, FIR e FIFO WASAPI. A admissão de uma janela de captura rejeitada como evidência indisponível exige pacotes íntegros, contíguos e frescos e dura menos de 500 ms acumulados. O novo modo preserva o limite original e continua recusando falhas de integridade. A ocupação usa cortes coerentes, com parada por lacuna e margem de um bloco. Sessão de até cinco segundos, razão fixa e sem comandos de drift. O render Float32/PCM16 com rampa de ganho foi exercitado exclusivamente em fakes. O teste de impulso observou início do sinal no terceiro bloco no relógio virtual, dentro de 30–34 ms pela convenção da fixture. O ensaio de mil codificações após aquecimento registrou zero bytes recorrentes. Nenhum dado estabelece latência acústica ou precisão de drift. [Protocolo, orçamento e prova preparada](docs/WASAPI_BACKEND_PLAN.md#f2-i6c-nominal-monitor--transporte-limitado-em-razão-fixa).

Restore aprovado com `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`. Builds finais com `dotnet build WaveStudio.slnx -c <configuração> --no-restore --no-incremental`, zero erros ou avisos em Debug e Release x64. Os direcionados usaram `--explicit off --filter-class '*WasapiNominal*'`. Um direcionado intermediário teve 13 de 21 aprovações. A fixture deixou a asserção de sentinela B1 ativa em buffers do novo fake que já haviam sido zerados. A fixture nominal passou a verificar SILENT nesse caminho, e as asserções dos fakes B1/B2 foram preservadas.

| Validação x64 | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | ---: | ---: | ---: | ---: |
| Direcionado nominal Debug | 21 | 0 | 0 | 6,824 s |
| Regressão Debug, seed 20261114 | 801 (780 + 21) | 0 | 4 | 81,220 s |
| Regressão Release padrão, seed 20261113 | 800 | 1 | 4 | 48,797 s |
| Regressão Release padrão, seed 20261114 | 800 | 1 | 4 | 45,620 s |
| Regressão Release serial, seed 20261113 | 801 (780 + 21) | 0 | 4 | 89,190 s |

A alocação histórica foi investigada nos casos do diário, EQ e resamplers nominal e adaptativo. Antes da mudança, os quatro passaram filtrados com seeds 20261113 e 20261114. Depois da mudança, passaram filtrados no Release com seed 20261113, tanto no padrão quanto no modo serial. As duas regressões Release padrão registraram 7.912 e 6.856 bytes, respectivamente, no teste do diário. A medição ocorre na thread do teste após aquecimento de 20 mil pares, com exigência de zero bytes nos 100 mil pares medidos. O loop, as asserções e a rotina de publicação testada foram preservados. A regressão Release completa serial passou com os mesmos binários e seed 20261113, preservando a asserção. O resultado diferencia os contextos do runner nesta amostra. Ele não identifica a causa da alocação, que permanece pendente. Atribuir esses bytes a JIT, infraestrutura de medição ou processamento exige diagnóstico adicional. Monitoramento físico audível e contínuo permanecem bloqueados por essa pendência.

Regressões executadas com `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`, sob política Restricted, com `--parallel none` na execução diagnóstica serial. Logs separados em `artifacts/f2-i6c-nominal`. O teste `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsRunBoundedSilentNominalTransport` foi descoberto pelo apphost Release como método único, sem executar Start. A prova SILENT nominal aguarda revisão e nova autorização. A Fase 2 permanece em desenvolvimento, sem início da F3.

## Histórico — F2-I6B2B-DIAG-PROOF, 18/11/2026

A única coleta física autorizada **não foi aprovada**. O runner Release x64 executou exclusivamente o teste dedicado, retornou código 2 e registrou uma falha em 5,060 s. A observação QPC encerrou em **3,7220736 s** dos 10 s configurados. O clock de captura rejeitou a leitura 37 por `WindowRateOutsideTolerance`. Em 2,0205647 s, avançou 96.480 frames para 96.987,1056 esperados. O resíduo de −507,1056 frames excedeu o limite de 486,935528. A janela móvel dos pacotes contíguos permaneceu dentro de seu limite. O padrão é compatível com uma diferença de fase próxima de um período, com causa física indeterminada.

Start, Stop e ReleaseBuffer retornaram S_OK em ambos os papéis. Os streams encerraram com liberação confirmada, sem buffers em voo nem recursos retidos. A saída ficou SILENT, com `CorrectionEnabled=false`, zero comandos, zero atualizações de Tracking e zero ppm aplicado. Não houve armazenamento de voz. A captura aceitou 371 pacotes contíguos, com 178.080 frames aceitos e um pacote inicial de 480 frames descartado. A FIFO produziu 178.080 frames e leu 176.160, com zero drops e zero underruns internos.

Houve 34 consultas diagnósticas, **20 cortes coerentes** e **zero janelas estimáveis**. `DurationReached`, `TransportObserved` e `CaptureTiming.Confirmed` ficaram falsos. Os 19 intervalos de ocupação entre cortes cobriram 3,636462 s, com média limitada entre 2.051,593 e 2.068,252 frames nesse trecho. Os dados não demonstram precisão repetível de 200 ppm nem sustentam correção física. [Reprodução numérica, histórico, contabilidade e critérios](docs/WASAPI_BACKEND_PLAN.md#f2-i6b2b-diag-proof--resultado-físico-18112026).

A `main` foi sincronizada por fast-forward de `558a431` a `bcafcda`, cuja diferença é exclusivamente documental. O Release x64 foi recompilado de `bcafcda` com .NET SDK 10.0.404, restore locked auditado e zero avisos ou erros. O proprietário reconfirmou os sete manuais aplicáveis e as condições imediatas. O Windows reportou um endpoint ativo e único por direção para o par confirmado. O runner validou formato exato e ausência do Wave Studio antes de Start. O filtro descobriu um único `Explicit`; a tentativa usou o apphost direto com as quatro variáveis de autorização limitadas ao processo, sem alterar a política Restricted.

O JSON original e os logs permanecem em `artifacts/wasapi-drift-diagnostic`, ignorados pelo Git. Prova 4, código-fonte e regressões anteriores preservados. Não houve nova tentativa. Controle físico, monitoramento audível e F3 aguardam revisão.

## Histórico — F2-I6B2B-DRIFT, 18/11/2026

A pasta oficial partiu limpa e a `main` avançou por fast-forward para `d80a5a1`. HEAD e `origin/main` foram conferidos, com Git 2.56.1, SDK .NET 10.0.404 e `git fsck --connectivity-only --no-dangling` aprovado. A prova silenciosa 4 e os sete casos manuais pós-formatação aprovados por relato foram preservados.

O caminho WASAPI foi integrado ao controle com fakes. A captura e o render publicam evidências de clock no corte do diário e o coordenador combina os intervalos de razão e ocupação. Somente o produtor aplica ppm, com geração, validade de até 500 ms, limites ±500 ppm e slew de 25 ppm/s. O render mantém SILENT. O aquecimento preenche a FIFO em razão nominal e contabiliza separadamente o priming do endpoint, as liberações antes do ticket e as liberações enquanto a FIFO aquece. Três segundos de janela, sobreposição de 2,5 s e faixa de até 200 ppm sustentam Tracking nos fakes com incerteza conhecida. Falhas de fronteira, informação perdida e comando expirado preservam o encerramento sem reativação.

No ensaio comparativo de 45 s virtuais a 48 kHz e +300 ppm no relógio da captura, a distância média da ocupação ao alvo nos últimos cinco segundos foi de 536,98 frames com razão nominal e 46,02 com correção adaptativa. O ppm aplicado terminou em −321,10 e a execução adaptativa terminou sem drops ou underruns. No ensaio de mil lotes do produtor, os dois builds registraram zero bytes recorrentes; os tempos da amostra foram 53,064 ms (Debug) e 9,688 ms (Release). Esses ensaios medem o caminho simulado e não caracterizam tempo real físico. Métricas numéricas em `artifacts/f2-i6b2b-drift`.

O restore passou com `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`. Os builds `dotnet build WaveStudio.slnx -c <configuração> --no-restore --no-incremental` terminaram com zero erros ou avisos em Debug e Release x64.

| Validação x64 | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | ---: | ---: | ---: | ---: |
| WASAPI, clocks e DSP adaptativo em Release | 311 | 0 | 3 | 35,220 s |
| Regressão Debug, semente 20261114 | 780 (758 + 22) | 0 | 3 | 73,302 s |
| Regressão Release, semente 20261114 | 780 (758 + 22) | 0 | 3 | 40,071 s |
| Reprodução filtrada dos casos de alocação em Release | 4 | 0 | 0 | 1,133 s |

O primeiro direcionado com as proteções finais encontrou 21/22 casos. O fake do wrap avançava dez blocos de captura em um ciclo e provocava um drop antes de sobrescrever o diário. Foi corrigida a cadência do fake, preservando ambas as proteções e as asserções. O log inicial está em `targeted-final-only-drift.log`.

As execuções completas Release com semente 20261113 registraram 778, 778 e 779 aprovações de 780 comuns. A primeira acusou 6.856 bytes no ensaio do diário e 3.992 no resampler adaptativo. A segunda registrou 2.632 bytes no EQ e 3.992 no resampler nominal. A terceira registrou 5.640 bytes no EQ. Os quatro casos passaram filtrados e na regressão completa com a semente alternativa, sem mudanças de código, asserções, configuração do runtime ou limites entre as execuções. A causa das alocações intermitentes segue indeterminada. Logs de falha preservados em `Release`, `Release-repeat` e `Release-third`, com a execução aprovada em `Release-alternate-seed`.

Os testes usaram o runner .NET direto, com política Restricted preservada. Comando da regressão `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`. O direcionado acrescentou os filtros `--filter-class '*Wasapi*' --filter-class '*Clock*' --filter-class '*Adaptive*'` e usou a semente 20261113. Os logs e diagnósticos ficam em `artifacts/f2-i6b2b-drift`.

O apphost Release descobriu um único teste futuro com `--list-tests text --filter-method 'WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsCollectSilentDriftEvidenceAtNominalRatio'`, sem executá-lo. A próxima coleta está preparada para dez segundos em SILENT e razão nominal, com trava de autorização específica e relatório sem voz ou identificadores de endpoint. A margem observada nas provas anteriores impede autorizar controle físico com a evidência atual. A coleta física, o monitoramento audível e F3 aguardam revisão. [Protocolo, incerteza e critérios da coleta](docs/WASAPI_BACKEND_PLAN.md#f2-i6b2b-drift--controle-com-evidência-intervalar-e-diagnóstico-silent).

## Histórico — F2-I6B2A-CHECKPOINT, 18/11/2026

A execução partiu da pasta oficial, com árvore limpa e `main` sincronizada por fast-forward com `origin/main` em `6404985`. Git 2.56.1, SDK .NET 10.0.404. A segunda consulta ao remoto manteve essa base antes da publicação. `git fsck --connectivity-only --no-dangling` passou e o ICO segue versionado. O histórico da prova 4 e os sete casos manuais pós-formatação aprovados por relato foram preservados.

O diário WASAPI integrado à sessão silenciosa é opt-in e exige backend injetado. Cada owner publica contagens e QPC locais na fronteira que sucede o ReleaseBuffer e a operação da FIFO. O coordenador aceita um corte se ambas as versões estáveis confirmarem que não havia buffer emprestado, com ticket válido e contabilidade compatível. A ocupação é em frames de saída. A média de um intervalo com histórico completo é apresentada em limites conservadores, pois os instantes individuais das mutações podem estar dentro de lotes sobrepostos. [Protocolo, ordenação, garantias e rejeições](docs/WASAPI_BACKEND_PLAN.md#f2-i6b2a-checkpoint--corte-coerente-dos-dois-owners-mta).

O cenário com fakes de 44,1 kHz mono → 48 kHz estéreo confirma priming, descarte de startup, conversão, drops e underruns, sem escrita de amostras nos buffers de render. Os novos casos também verificam falhas de Get/Release, timeout, cancelamento e retenção do guard. A publicação passou no ensaio de 200 mil registros sintéticos com zero bytes recorrentes no thread medido, teto de 20 KiB de alocação da fixture e orçamento de cinco segundos. Os históricos pré-alocados da implementação consomem 16 KiB; o ensaio mede operação sintética, sem medir tempo real físico.

O restore locked com auditoria passou com `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`. Builds com `dotnet build WaveStudio.slnx -c <configuração> --no-restore --no-incremental` passaram em Debug e Release x64, com zero erros e avisos. O direcionado WASAPI em Debug teve 235 aprovados e dois Explicit excluídos em 23,158 s. A classe `WasapiCheckpointTests` passou em Release com 19 aprovações em 3,311 s. Duas variantes adicionais da suíte existente completam os 21 novos casos líquidos.

| Regressão x64 | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | ---: | ---: | ---: | ---: |
| Debug | 758 (737 + 21) | 0 | 2 | 75,658 s |
| Release | 758 (737 + 21) | 0 | 2 | 41,239 s |

As regressões passaram na primeira execução, sem reincidência das falhas intermitentes de alocação já documentadas. Comando `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`, com seeds 20261114 em Debug e 20261113 em Release. O runner .NET foi chamado diretamente sob a política Restricted. Logs em `artifacts/f2-i6b2a-checkpoint`, incluindo os subdiretórios `Debug`, `Release` e `target-final`. O filtro do direcionado WASAPI foi `--filter-class '*Wasapi*'` e o do diário, `--filter-class '*WasapiCheckpointTests'`.

O caminho B1 continua com a instrumentação desativada, render SILENT e critérios de `TransportObserved` preservados. A etapa teve zero provas físicas. Os resultados se limitam à contabilidade e ao ciclo de vida com fakes. F2-I6B2B e F3 continuam pendentes de revisão e autorização.

## Histórico — COMENTARIOS-PTBR, 18/11/2026

A main foi sincronizada por fast-forward a `80a0f59` e, durante a validação, ao commit documental `79a6867`. Foram examinados 125 arquivos versionados de código, testes, scripts e configuração. Cada comentário alterado foi contado como uma unidade sintática, incluindo blocos e documentação XML.

| Escopo | Arquivos examinados | Arquivos traduzidos | Comentários traduzidos |
| --- | ---: | ---: | ---: |
| C# em src e tests | 112 | 72 | 203 |
| Scripts PowerShell | 3 | 1 | 5 |
| Projetos, props e solução XML | 5 | 1 | 1 |
| Configuração e locks JSON | 4 | 0 | 0 |
| .gitignore | 1 | 0 | 0 |
| Total | 125 | 74 | 209 |

Em C#, 126 comentários de 39 arquivos de áudio, 23 de 12 arquivos da interface e 54 de 21 arquivos de teste foram traduzidos. O total inclui 193 comentários de linha, 5 blocos e 5 comentários de documentação XML. Foram preservados os dois comentários literais `INPROC_SERVER` e `+0.25 PCM16`, o texto do modelo de terceiros em `.gitignore`, a string funcional que gera o probe do watchdog e seu marcador `WaveStudio-WatchdogProbe-v1`. Os textos de licença, arquivos gerados, assets e backups ficaram fora da tradução.

A equivalência dos 112 arquivos C# foi verificada com Roslyn do SDK instalado, comparando árvore sintática, tipos e textos de tokens, valores de strings, diagnósticos de parse e texto fora dos comentários. As cinco documentações XML foram comparadas por estrutura, incluindo tags e atributos. O parser do PowerShell comparou os três scripts, preservando tokens executáveis, diretivas e strings. Os cinco arquivos XML de configuração foram comparados por estrutura e texto fora de comentários. Os quatro JSON e o `.gitignore` permaneceram textualmente iguais ao baseline. As edições conservaram as quebras de linha e a codificação originais. `git diff --check` passou. Inventário, patch e verificações locais em `artifacts/comment-audit`.

O restore passou com `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`. Os builds `dotnet build WaveStudio.slnx -c Debug --no-restore --no-incremental` e equivalente em Release x64 terminaram com zero erros ou avisos. As regressões do runner .NET direto usaram `dotnet test --solution WaveStudio.slnx -c <configuração> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`. Política Restricted preservada, seeds 20261114/20261113 e logs em `artifacts/comment-audit/Debug` e `artifacts/comment-audit/Release`.

| Configuração x64 | Aprovados | Falhas | Explicit excluídos | Duração da regressão |
| --- | ---: | ---: | ---: | ---: |
| Debug | 737 | 0 | 2 | 72,995 s |
| Release | 737 | 0 | 2 | 42,257 s |

As duas suítes passaram na primeira execução deste checkpoint, sem repetir as falhas intermitentes de alocação já registradas no histórico.

Os sete casos manuais aprovados por relato e as evidências da prova silenciosa 4 foram preservados. Nenhuma prova física foi executada. A etapa está pronta para revisão. F2-I6B2, F3 e internacionalização seguem pendentes.

## Histórico — F2-I6B1-PROOF-4 aprovada pelo teste

- Fast-forward para `d41a6f7`. Release x64 existente da implementação `f72304e`, SDK 10.0.404 e filtro de um único teste conferidos. Autorização específica e confirmação de headset conectado, Wave Studio fechado normalmente e ausência de chamadas/testes. Verificados endpoints exatos, ativos e únicos por direção e ausência de outros runners. Os sete manuais pós-formatação permanecem aprovados por relato.
- A tentativa única SILENT passou com `TransportObserved=true`, `DurationReached=true` e `CaptureTiming.Confirmed=true`. O temporizador configurado para 5 s concluiu. O QPC registrou 4,9997441 s, 0,2559 ms abaixo do nominal. Runner com código 0, um teste aprovado.
- A captura aceitou 499 pacotes contíguos (239.520 frames) e descartou o primeiro pacote descontínuo (480 frames). Houve 29 janelas de clock, 298 de pacotes e erro terminal de corroboração de 0,235279 frame, com limite de 1.158,792512. O histórico registra seis leituras provisórias na captura, sem rejeição. O render permaneceu estrito.
- Start/Stop retornaram S_OK em ambos os papéis. Streams liberados, sem recursos retidos ou buffers em voo. Saída SILENT e nenhuma voz armazenada. [Medições, históricos e contabilidade](docs/WASAPI_BACKEND_PLAN.md#f2-i6b1-proof-4--resultado-físico-18112026).
- Código e critérios preservados. Sem repetição da prova, build ou regressão completa. Resultado sujeito a revisão, com F2-I6B2 e F3 pendentes. O resultado não determina a origem das falhas anteriores nem valida drift físico ou monitoramento audível.

## Histórico — F2-I6B1-CLOCK-WINDOW

- Sincronização inicial por fast-forward a `38d3e5d`. O replay dos 11 pontos por papel reproduziu os −480,624 frames do intervalo rejeitado e −479,6592 frames desde a primeira leitura da captura. O desvio é compatível com variação de aproximadamente um período, sem explicação física comprovada. O replay classifica a última leitura como provisória; os dados da prova 3 continuam insuficientes para aprovação.
- Implementação restrita à captura SILENT. Margem de até um período consultado no curto prazo e desde âncora fixa, com validação nominal em janelas móveis de 2–2,5 s e corroboração pelos deltas dos pacotes contíguos já liberados. A política estrita continua no render, na simulação e no estimador. [Análise, fórmula e limites](docs/WASAPI_BACKEND_PLAN.md).
- Restore locked com auditoria NuGet aprovado. Builds Debug/Release x64 com zero erros ou avisos. Direcionados WASAPI: 214 aprovados. Regressões finais: 737 aprovados (696 anteriores + 41 casos), zero falhas e dois Explicit excluídos por configuração. Debug 71,183 s e Release 39,632 s, runner .NET direto com política Restricted, prazo 165 s e seeds 20261114/20261113. Logs em `artifacts/f2-i6b1-clock-window`. As primeiras rodadas mediram alocação inesperada em dois testes existentes (EQ em Debug, 6.040 bytes; consumo parcial do resampler nominal em Release, 3.992 bytes). Os casos passaram isoladamente e nas repetições completas, sem mudanças de código, parâmetros ou asserções entre execuções. A causa das ocorrências permanece indeterminada.
- Nenhuma execução física, nova prévia ou ajuste do controlador de drift. Os sete testes manuais permanecem aprovados por relato. Prova 4 aguarda revisão e autorização específica. F2-I6B2/F3 seguem pendentes.

## Histórico — F2-I6B1-PROOF-3 não aprovada

- Sincronização inicial por fast-forward para `38d49d2`, com binário Release x64 existente da implementação `97b2c99`, SDK 10.0.404 e um único teste descoberto por filtro exato. O proprietário confirmou autorização de cinco segundos, headset conectado, Wave Studio fechado normalmente e ausência de chamadas/testes. Confirmados os dois endpoints ativos e únicos por direção, além da ausência do app e de outros runners. Os sete casos manuais permanecem aprovados por relato.
- A tentativa única SILENT encerrou a observação após **1.110,3045 ms**. O clock da captura rejeitou a leitura 11 por `AdvanceOutsideTolerance`: 5.280 frames calculados para 120,013 ms de QPC, 5.760,624 esperados, erro de 480,624 contra limite de 30,80312. Ambos os HRESULTs nativos dessa leitura foram S_OK. A origem da diferença continua indeterminada, assim como o ramo específico da prova 2.
- O descarte inicial consumiu 480 frames do primeiro pacote com DATA_DISCONTINUITY. A captura aceitou 111 pacotes e 53.280 frames. A FIFO produziu 53.280 e leu 52.320 frames. Os dois históricos terminais contêm 11 leituras, sem sobrescrita, com nove avanços válidos na captura e dez no render. O render encerrou sem rejeição de clock.
- Ambos Start/Stop retornaram S_OK, com liberação confirmada, sem recursos retidos ou buffers em voo. Saída sempre SILENT, sem armazenamento de voz. `DurationReached=false`, `MovementProven=false` e `TransportObserved=false`, um teste, uma falha e código 2. [Histórico e cálculo reproduzido](docs/WASAPI_BACKEND_PLAN.md#f2-i6b1-proof-3--resultado-físico-18112026).
- Código, limites e parâmetros preservados. Nenhuma repetição da prova, regressão completa ou alteração dos backups. F2-I6B2 e F3 permanecem pendentes.

## Histórico — F2-I6B1-CLOCK-DIAG

- Sincronização inicial por fast-forward a `139a8e4`. A investigação partiu do JSON local da prova 2 e dos contratos do WASAPI. Os avanços aceitos excluem a espera de startup. A ausência de leituras anteriores impede identificar qual regra gerou `InvalidPosition`. A posição de pacote não serve para reconstruir o delta de `IAudioClock`.
- Implementados motivos precisos e histórico de 64 leituras por owner, com última leitura aceita e primeira rejeitada em campos próprios. O relatório terminal contém âncora, deltas, resto, avanço, expectativa, erro e limite aplicado quando disponíveis. A cópia ocorre após join; o histórico fica ausente enquanto a liberação está pendente. Sem IDs, nomes ou PCM. [Análise e critérios](docs/WASAPI_BACKEND_PLAN.md).
- Restore locked com auditoria NuGet aprovado. Builds Debug/Release x64 com zero erros ou avisos. 173 testes direcionados passaram. Regressões completas: 696 aprovados (663 anteriores + 33 novos), zero falhas e dois Explicit excluídos por configuração. Debug 71,651 s e Release 42,344 s. Runner .NET direto com política Restricted preservada, prazo de 165 s, seeds 20261114/20261113 e logs em `artifacts/f2-i6b1-clock-diag`. Nenhuma prova física executada.
- As decisões de validação e os limites existentes foram preservados. A prova 2 permanece sem aprovação. A prova 3 aguarda revisão e autorização específica, e a F2-I6B2 segue pendente. Os sete testes manuais já aprovados permanecem registrados.

## Histórico — F2-I6B1-PROOF-2 não aprovada

- Sincronização inicial por fast-forward para `212a60c`. Binários Release x64 existentes da correção `c7ff2c9`, SDK 10.0.404 e descoberta de um único teste com filtro exato conferidos. Autorização específica registrada. O proprietário confirmou headset conectado, Wave Studio fechado normalmente e ausência de chamadas ou testes. Verificados endpoints exatos, únicos e ativos por direção, além da ausência do app e de outros runners. A01/A02/A03/A05/A09/A11/A12 permanecem aprovados por relato.
- Uma execução `Explicit`, com saída sempre SILENT. O primeiro pacote foi descartado integralmente (480 frames, DATA_DISCONTINUITY, 0x1). A captura aceitou 41 pacotes seguintes, com 19.680 frames produzidos e 18.720 lidos na FIFO. O descarte inicial funcionou nesta tentativa.
- A observação parou em **412,5004 ms** após `InvalidPosition` no clock da captura, apesar de o último GetPosition ter retornado S_OK. Houve dois avanços válidos na captura e três no render antes da rejeição. O snapshot não identifica a condição exata que disparou `InvalidPosition`. `DurationReached=false`, `MovementProven=false` e `TransportObserved=false`. Runner: um teste, uma falha, código 2.
- Ambos Start e Stop retornaram S_OK. O encerramento liberou os dois streams, sem buffers em voo ou recursos retidos e sem armazenamento de voz. Evidências numéricas no [plano WASAPI](docs/WASAPI_BACKEND_PLAN.md). Não houve repetição, mudança de código ou regressão completa. A prova requer revisão, e F2-I6B2 e F3 permanecem pendentes.

## Histórico — F2-I6B1-FIX

- Sincronização inicial por fast-forward até `ea1d499`. O transporte agora descarta apenas o primeiro pacote não vazio com DATA_DISCONTINUITY, isolada ou combinada com SILENT, se chegar dentro de 500 ms a partir da chamada a Start e passar as validações existentes. O pacote é consumido por inteiro e descartado antes do FIR/FIFO. A exceção não se aplica a perdas posteriores.
- Contabilidade de captura distingue adquirido, liberado, aceito e descartado no startup. A aprovação exige produção e leitura efetivas da FIFO, clocks válidos dos dois streams, janela completa e encerramento confirmado. Detalhes e contratos em [WASAPI_BACKEND_PLAN](docs/WASAPI_BACKEND_PLAN.md).
- Restore locked com auditoria NuGet e erro para indisponibilidade (NU1900) aprovado. Builds x64 Debug/Release sem avisos ou erros. Testes direcionados WASAPI: 140 aprovados. Regressões: 663 aprovados (644 anteriores + 19 casos líquidos) e dois Explicit excluídos por configuração, zero falhas. Debug 70,669 s e Release 39,645 s. Runner .NET direto com política Restricted preservada, limite de 165 s, seeds 20261114/20261113 e logs em `artifacts/f2-i6b1-fix`. A primeira compilação identificou uma incompatibilidade de tipos numa asserção nova, corrigida para comparar uint.
- A primeira prova física continua sem aprovação. Nenhum teste Explicit ou caso manual foi repetido nesta correção. F2-I6B1-PROOF-2 depende de nova revisão e autorização específica. F2-I6B2 permanece pendente.

## Histórico — F2-I6B1-PROOF, tentativa física e parada antecipada

- Sincronizado por fast-forward até `dd2e376`. A01, A02, A03, A05, A09, A11 e A12 aprovados por relato do proprietário após a formatação. O proprietário confirmou a autorização de cinco segundos, headset USB conectado, fechamento normal do Wave Studio e ausência de chamadas e testes em andamento. A checagem local confirmou ausência do app e de outro runner, e um endpoint ativo com o nome exato em cada direção.
- Executado uma vez em Release, SDK 10.0.404, por filtro exato de `WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport` e `--explicit only`. O processo de teste foi o único a receber as variáveis opt-in. O código e os binários conferidos correspondem à implementação `bed126c`. Nenhuma regressão ou teste manual foi repetido.
- Resultado: **prova não aprovada**. Ambos os Start retornaram S_OK. O primeiro pacote de captura tinha 480 frames e flag 0x1 (DATA_DISCONTINUITY). O backend interrompeu a observação em 11,0401 ms, rejeitou o pacote antes de ler PCM e chamou ReleaseBuffer(0) com S_OK. A saída permaneceu SILENT, com 1.056 frames de priming e 1.536 frames liberados no total. Sem voz armazenada.
- Stop retornou S_OK nos dois papéis, com liberação confirmada, nenhum buffer em voo e nenhum recurso retido. A janela de cinco segundos e o critério de movimento dos clocks ficaram sem confirmação. Exit code 2, um teste executado, uma falha. O [plano WASAPI](docs/WASAPI_BACKEND_PLAN.md) registra os dados técnicos. Uma avaliação da descontinuidade antecede nova execução. F2-I6B2 permanece pendente.

## Histórico — preflight inicial da F2-I6B1-PROOF

- Sincronização segura para `07b625d`. Esse commit alterou somente documentação em relação à implementação `bed126c`. Conferidos SDK 10.0.404, Desktop Runtime 10.0.15 e binário Release existente. A descoberta com filtro exato e `--explicit only` encontrou um teste. Nenhuma suíte foi repetida.
- O proprietário autorizou especificamente a captura de cinco segundos com saída SILENT e sem armazenamento de voz. Nenhum processo Wave Studio apareceu na checagem. O Windows listou mais de uma entrada e saída. A confirmação de headset conectado, par exato de endpoints ativos, fechamento normal, ausência de chamadas/testes e resultados A01/A02/A03/A05/A09/A11/A12 no ambiente reinstalado ainda está pendente.
- Resultado deste preflight: prova física pendente. Nenhum Start ou GetBuffer físico foi executado e nenhum resultado de clocks, frames, padding ou Stop/Release foi medido nesta etapa. [Inventário da recuperação e proposta de organização](docs/RECOVERY_AUDIT.md).

## Estado atual — recuperação e F2-I6B1 sem execução física

- Recuperação em Windows x64, build 19045, com Git 2.56.1.windows.1, SDK 10.0.404 e Desktop Runtime 10.0.15. A configuração Codex existe e a sessão opera com workspace-write e aprovação sob demanda. Autenticação/consulta e fetch do GitHub confirmados.
- O backup integral fora do repositório contém 3.182 arquivos verificados por SHA-256, incluindo .git e arquivos ignorados. Os diffs binários de HEAD e do índice foram exportados à parte no mesmo backup. O `git fsck --full` terminou com código 0 e listou apenas objetos pendentes. O status inicial mostrou a exclusão preparada do ICO e oito arquivos preparados da F2-I6B1, sem alterações adicionais ou arquivos não rastreados. A origem da exclusão no índice ficou indeterminada.
- Restaurado somente o ICO de HEAD, blob `636df116b2482feb97a1ae98cb415ab03dab14ce`, igual ao blob no GitHub. `main` avançou de `db0d83a` para `81f53c9` com fast-forward de sete documentos. Os oito caminhos preparados conservaram os mesmos blobs no índice durante a sincronização.
- A F2-I6B1 reutiliza os oito arquivos recuperados. Priming/saída SILENT, ciclo de vida/packets por owner MTA, validação dos clocks, processamento fora dos buffers emprestados e retenção do guard quando Stop/Release permanecem incertos. O diagnóstico curto conserva o guard após falha de Dispose. Detalhes de ABI e limites no [plano WASAPI](docs/WASAPI_BACKEND_PLAN.md).
- Restore locked com auditoria NuGet e erro para indisponibilidade da auditoria (NU1900) passou. Builds Debug/Release x64 recompilados com zero erros ou avisos. A política Restricted bloqueou o watchdog PowerShell. As regressões usaram o runner .NET/MTP diretamente com seeds 20261114/20261113, limite de 165 s e logs em `artifacts/recovery-final`, com a política preservada.
- Regressões finais: 644 aprovados por configuração (603 anteriores + 41 novos), 0 falhas e 2 Explicit excluídos em cada uma. Debug 71,063 s e Release 39,794 s. A primeira Release teve 643 aprovados e uma falha no teste de alocação do EQ (7.192 bytes medidos, zero esperados). O teste passou isolado e a repetição completa passou sem mudança no código de EQ, fixture, prazo ou asserção. A causa da ocorrência permanece indeterminada.
- A prova silenciosa `Explicit` aguarda autorização específica e pré-condições físicas. Nesta recuperação não houve Start nativo de áudio nem captura física ou reprodução contínua. Controle de drift em hardware, F2-I6B2, F3 e aceitação física seguem pendentes. A prévia instalada continua no pacote anterior. Dependências, locks, tarefa e revisão não editados por esta implementação.

## Histórico — F2-I6A, clock WASAPI e preparação sem áudio

Implementação, testes de preparação sem captura/reprodução, commit e push autorizados pelo proprietário. Árvore inicialmente limpa em c1d4c88; sincronizada por fast-forward com origin/main para8a9e116. Remoto Wave-Universal-Studio/main, .NET SDK10.0.404 e Git2.56.0 confirmados. AGENTS/NEXT_TASK/REVIEW e requisitos/arquitetura/status, planos WASAPI/motor/mestre e check-up consultados. Normativos, tarefa/revisão, dependências/locks, UI, EQ/perfis/headroom/FIR/SPSC e scripts preservados. **A14 aprovado por relato geral do proprietário**, conforme revisão sincronizada; não inventados valores/subcasos ou novas aprovações físicas.

| Área | Implementação / evidência F2-I6A |
| --- | --- |
| Fonte/ABI | Fonte NAudio3.1.0 fixada e IDLs Microsoft conferidos. IAudioClock/GetPosition público do wrapper perde S_FALSE; interfaces nativas da biblioteca são internas. Implementada ponte mínima própria das interfaces Windows, PreserveSig, GUIDs/ordem e marshaling auditados |
| Recursos | Source generation .NET com ComObjectWrapper, wrapper único e referências cruas balanceadas; aquisição/query/release no mesmo MTA. CoInitializeEx/CoUninitialize balanceados, liberação clock→cliente/formato→dispositivo/enumerador→apartamento→lease |
| Relógio | HRESULTs originais e ulong de posição/frequência/QPC preservados, DeviceUnits e QPC100ns; S_OK Accurate, S_FALSE Uncertain, falhas/frequência inválida Unavailable. Overload reutiliza WasapiClockAdapter, sem relaxar validação, estimador ou FIR novo |
| Preparação | Endpoint explícito/ativo/direção revalidado, mix exato PCM16/float32 mono/LR44,1/48kHz validado; shared Initialize flags0/duração0/período0, GUID de sessão próprio do pedido, nenhum fallback/AutoConvert/Loopback. Só consulta de capacidade/latência e clock da mesma sessão |
| Segurança | Nenhum Start, GetBuffer de captura/render ou conteúdo gravado/reproduzido. Guard realmente compartilhado no processo com diagnóstico de produção, lease até quiescência/liberação; cancelamento não aborta chamada COM em voo. Release que falha conserva filho/pais/lease, sem retry em outro owner |
| Testes novos |46 automáticos de HRESULT/inteiros/flags/identidade/formato/cancelamento/falha parcial/ownership/guard e1 Explicit de preparação real.557 anteriores preservados. Conjunto direcionado final189 aprovado |

Prova nativa própria e isolada autorizada, Debug,1/1 teste explícito aprovado: prévia WaveStudio.exe fechada nas verificações, seleção por nomes únicos “fifine” (captura) e “USB Audio Device” (saída), IDs ativos fixados por direção/geração e revalidados sem seguir padrão/fallback. **Ambos inicializados e referências próprias liberadas, sem iniciar áudio**. Float32/48kHz, mono máscara4/align4 e LR máscara3/align8. Ambos reportaram período padrão100.000 e mínimo exclusivo30.000 ×100ns, buffer1.056 frames e StreamLatency0. Clock da própria sessão: GetFrequency S_OK,192.000/384.000; GetPosition S_OK, posição0 nos dois; QPC100ns0/103.502.894.326. Relatório sem IDs/nomes/conteúdo em artifacts/wasapi-preparation/native-preparation.json, ignorado pelo Git. Não há clock em movimento comprovado; não alimentar estimador com essas leituras paradas. Latência reportada0 e capacidade22ms não significam latência física zero ou medição fim a fim.

Limites: guard é por processo, não mutex global; prova exige prévia fechada e opt-in. Chamada nativa bloqueada pode reter worker/lease até retornar, sem cancelamento forçado. Se Release falhar, recursos são enraizados e outro preparo/diagnóstico é recusado; revisão/reinício será necessário, sem promessa de retry universal. Nenhuma falha de driver/release foi provocada fisicamente. Não foram alterados níveis/mute/padrões, configurações críticas, drivers, segurança ou áudio de outros aplicativos. Sem benchmark novo, atualização/instalação de dependência ou republicação da prévia, pois não houve mudança visível. Executável local permanece no pacote EQ-PROFILE-1; fonte inicia com `dotnet run --project src/WaveStudio.Desktop`.

Ocorrências durante edição:3 erros de acessibilidade em parâmetros públicos de testes corrigidos;9 avisos de analisador corrigidos sem supressão. Consulta `dotnet test -- --help` produziu FailFast do CLI10.0.404 antes de casos; ajuda obtida do apphost existente, sem modificar runner. Primeira prova nativa expôs EntryPointNotFound em GetDevice: opção ManagedObjectWrapper gerava o sentido inverso do marshaling; finalmente liberou recursos, alterado para ComObjectWrapper e repetido com sucesso. Não houve Start como contorno, nem API/vtable privada. Testes comuns/físicos anteriores não foram reclassificados por esse resultado.

Verificações finais: restore locked auditado (`dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all`); builds Debug/Release x64 --no-restore com 0 erros/avisos. **603 aprovados em Debug e 603 em Release**, 0 falhas e 1 Explicit excluído em cada suíte (604 descobertos), 70,062s/43,054s. Esse Explicit já foi exercitado separadamente 1/1, não repetido automaticamente pela regressão. Watchdog existente com seeds20261114/20261113, timeout180/MTP165; sem kill, pipes completos e sem alterar prazos/fixtures ou ExecutionPolicy. Logs: artifacts/clock-drift/Debug-20261114-c1508721d6974b8081365292b35c3efa e Release-20261113-b3ccafa859544395a36634bb39ae2199.

Documentação ABI/ownership/unidades e resultados em [WASAPI_BACKEND_PLAN](docs/WASAPI_BACKEND_PLAN.md); desenho mínimo F2-I6B apenas documentado, incluindo workers/packets, priming real, ticket, ordem start/stop e checkpoint conservador em lugar de watermark fake. Transporte nativo ativo, drift/precisão em movimento, monitoramento e F3 continuam pendentes de revisão/aceitação pertinente/autorização própria. A01–A13 restantes, USB/suspensão e Windows11x64 pendentes; Windows10 experimental. Alpha/Fase2 não aceitas. Parar após publicação desta etapa.

Conferência do conteúdo versionável e `git diff --check` sem erros; nenhum segredo, ID real, configuração pessoal, temporário ou artefato de compilação incluído. Relatórios/probes e pacote local permanecem ignorados. Commit/push desta etapa autorizados; sincronização final da árvore deve ser conferida após a publicação, sem avançar a próxima tarefa.

## Histórico — EQ-PROFILE-1, ajustes e perfis do EQ da prévia

Implementação, testes, commit/push e republicação local autorizados pelo proprietário. Árvore inicialmente limpa em faaac35, sincronizada por fast-forward com origin/main em d4f5f47; remoto Wave-Universal-Studio/main confirmado. Lidos AGENTS, NEXT_TASK, REVIEW, requisitos, arquitetura/status e documentação pertinente. Normativos, tarefa/revisão e plano mestre preservados.

Implementados modelos imutáveis de ajustes/perfis com cópias independentes e IDs estáveis. Até20 perfis; nomes normalizados de1 a48 caracteres, únicos sem distinguir maiúsculas; Padrão reservado. Dez bandas −15/+15 dB em passos de1 e entrada/preamp −30/0 em passos de0,1. Ajustes atuais e ativação persistem separados do perfil salvo: edição não o sobrescreve. UI simples com Criar perfil, Salvar alterações, Renomear, Excluir confirmado e aviso ao substituir ajustes diferentes. Padrão neutro/desligado; reset existente conserva ativação. Exclusão mantém ajustes atuais.

JSON v2, migração explícita v1→v2 somente em memória na abertura, preservando seleções, níveis, janela e defaults históricos de coordenadas. Próximo save grava2 e mantém original no backup; recuperação de backups1/2, futuro/conflito/corrupção e limites existentes preservados. SettingsStore não reescrito. Gestos consolidados, debounce500 ms, fila existente e flush ao fechar após aguardar recuperação/reset. Falhas informadas, retry explícito possível; arquivo protegido permanece protegido. Binding inicial não salva nem inicia áudio. Controles aguardam carga e ficam congelados durante atividade/fechamento; formato não suportado preserva parâmetros sem habilitar EQ.

Clipe continua vazio após reiniciar, nível digital de teste20%; ativação salva não autoriza captura/playback. Nenhuma escrita de volume/mute/padrão Windows. Audio/DSP/headroom/FIR/clocks/backend/COM e dependências/locks preservados. Biblioteca Audio publicada mantém exatamente o SHA-256 do CHECKUP-1. Sem novos efeitos globais, drivers, redesign, instalador ou release pública.

38 testes novos (31 modelos/JSON/armazenamento e7 UI, incluindo4 integrações de janela/arquivos). Cobrem migração sem escrita inicial, roundtrip, cópias/CRUD, limites/JSON estrito, backup1/2, futuro/conflito, falha de save/retry, arraste/flush, duas aberturas e rascunho distinto, ausência de áudio/clipe,20% e congelamento. Testes anteriores de versão futura passaram de2 para3, pois2 é agora suportada; nenhuma asserção removida. Casos novos inicialmente identificaram aceitação de propriedade calculada ignorada e defaults v1 de janela incompatíveis com parâmetros obrigatórios; corrigidos por método não serializável e migração explícita dos defaults, respectivamente.

Primeira regressão Debug556/557: fixture MeterVisibility excedeu prazo no fechamento, também reproduzido isoladamente. Reutilizado loop STA assíncrono de PreferencesUiTests, sem modificar produção de medição;2 direcionados aprovados. Regressão seguinte encerrou após489 aprovados, com exceção de layout em continuação de MicrophoneTestPanel.RunAsync na thread pool. Fixture de microfone sem loop real convertida para o mesmo helper existente;17 testes direcionados passaram. Prazos e assertivas preservados. Não atribuir essas correções à causa histórica completa de intermitência. Resultados finais abaixo substituem essas execuções intermediárias.

Verificações: restore locked auditado (`dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all`), builds Debug/Release x64 --no-restore com 0 erros/avisos. Regressões finais: **557 aprovados em Debug e 557 em Release**, 0 falhas/ignorados, 69,248 s/41,027 s; watchdog sem kill, pipes completos. Executadas com scripts/Test-WithWatchdog.ps1, seeds 20261114/20261113, TimeoutSeconds180 (MTP165), sem alteração do script ou ExecutionPolicy. Logs finais: artifacts/clock-drift/Debug-20261114-8a29790fd40d45f09a6af5c1b4fcdb35 e Release-20261113-204664cbb37f4f4da5f14d391d40689d.

Publicação Release x64 framework-dependent em staging-eq-profile-1-20261114, sem símbolos; PE AMD64, produto Wave Studio/FileVersion0.1.0.0 e marca incorporada preservados. Instância própria com settings exclusivo abriu visível, respondeu e fechou normalmente com código 0, gravando JSON2, sem áudio; não houve inspeção visual independente. Requer .NET Desktop Runtime10 x64. Executar a pasta completa em `artifacts/alpha-preview/win-x64/WaveStudio.exe`, ou pela fonte `dotnet run --project src/WaveStudio.Desktop`. Artefatos locais/validação ficam fora do Git.

| Arquivo | Bytes | SHA-256 |
| --- | --- | --- |
| WaveStudio.exe |303.616|97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285|
| WaveStudio.dll |537.088|0FB4BAE8316643969977BC3FEFCD2051C2C6DEB2B9C5BD681801B2E82CDBB7BF|
| WaveStudio.Audio.dll |200.704|CCD1FB262E646F89BE6F83327E211C5D272A8729475460010FA8FA47ABABCB85|

Pacote promovido após confirmar ausência de WaveStudio.exe aberto, sem forçar fechamento: 16 arquivos conhecidos (8 runtime, 4 documentos, 4 licenças), hashes de destino iguais ao staging. Backup anterior em artifacts/alpha-preview/backup-before-eq-profile-1-1f79f89b483c4d899ab0666b513ea422; manifesto e startup em artifacts/alpha-preview/validation/eq-profile-1. Preferências/arquivos desconhecidos preservados. Conteúdo versionável restrito a código, testes e documentação da tarefa; sem credenciais ou artefatos de compilação encontrados. `git diff --check` sem erros. Commit/push autorizados desta etapa, sem antecipar a seguinte.

Pendências físicas: **A14** no [roteiro Alpha](docs/ALPHA_ACCEPTANCE.md), fechar/reabrir com o mesmo arquivo, valores/ativação/rascunho/perfil, confirmar trocas/exclusão, clipe vazio e nível20%, congelamento e layout/DPI. Proprietário já relatou diferença audível original/EQ, sem aprovação dos demais subcasos A13. A01–A13 restantes, USB/retomada e Windows11x64 pendentes; Windows10 experimental. Não executados novos testes físicos de áudio pelo agente. Alpha/Fase2 não aceitas. Após publicação, parar para revisão; F2-I6A continua próxima etapa autorizável, não iniciada.

## Histórico — CHECKUP-1, encerramento e recursos

14/11/2026: execução, testes, commit/push e republicação local autorizados. Main inicialmente limpa em af2ba4b, sincronizada por fast-forward para645921d; remoto Wave-Universal-Studio/main confirmado. AGENTS/NEXT_TASK/REVIEW/GENERAL_CHECKUP, requisitos, arquitetura/status e trechos pertinentes lidos. Preservados normativos/tarefa/revisão e plano mestre; não implementados novos efeitos, backend/COM/clock, drivers, monitoramento ou redesign.

| Categoria | Resultado CHECKUP-1 |
| --- | --- |
| Causas reproduzidas |9 testes inicialmente falharam:8 captura/reprodução×discard/select/invalidation/close com erro deixavam Busy true;1 cancelamento pré-Start deixava provider/modo. Corrigidos sem apagar original indevidamente |
| Liberação |Completion com erro não pula cleanup. Cada stream concluído/pré-Start recebe uma tentativa de Dispose; erros primário/cleanup separados. Nunca força Dispose enquanto Completed não confirma término após tentativa de Start |
| Entrega/worker |Época de entrega invalidada antes de stop/falha e buffer selado. RequestStop/Tick tratados no controle, nova operação bloqueada enquanto Busy; falha retida/estado pendente, sem restart/fallback automático |
| Timeout/retry |DisposeAsync aguarda até 3 s por chamada sem abortar nativo; mantém referências/worker e permite nova espera. Exceção terminal inesperada invalida seleção e informa recursos não confirmados, sem descartá-los à força |
| Start NAudio |Wrappers de teste one-shot; fonte pública 3.1.0 auditada: inicialização síncrona/Thread.Start final. Falha síncrona do primeiro Start não é confundida com callback ativo; falhas assíncronas continuam nos eventos Stopped. Nenhuma API privada/reflection |
| Janela |FormClosing protegido, cleanup independente de mic/startup/settings/catálogo/endpoints/mixer/watch. Tarefas pendentes/sucesso reutilizadas, falhas podem ser tentadas novamente. Espera global 5 s; janela/caption permanecem disponíveis e conteúdo bloqueado se encerramento não confirmado |
| Snapshot fake |Snapshot nulo/exceção→SnapshotUnavailable, gate inválido e média null. Flag de disponibilidade e últimos contadores conhecidos, sem zerar/inventar leitura atual. Teste de callback durante Start preserva priming e recusa entrega |
| Preservado |EQ/headroom/original A/B, FIR/nominal/adaptativo/SPSC, comandos Windows, sessões, preferências/backup, marca, SDK/dependências/locks e scripts de watchdog |
| Novos testes |24:19 engine/worker,2 STA de fechamento (Windows somente leitura/janela),3 de transporte fake. Inclui Stop/Tick que falha, chamada bloqueada, erros primário+Dispose, deadline sem descarte ativo e retry |
| Verificações |Restore locked auditado, builds Debug/Release x64 sem erros/avisos; **519 aprovados por configuração** (495+24),0 falhas/ignorados. Debug61,510 s/Release30,756 s |

Testes direcionados:9 falhas antes do patch confirmaram os defeitos, depois17 novos passaram e54 no conjunto novo+transporte existente. Conjunto amplo intermediário167 aprovados; ampliação final acrescentou o caso worker-close de reprodução. Dois preparos iniciais do teste de aviso de armazenamento encerravam a janela antes de executar Shown, portanto não acionavam o erro; teste passou a aguardar startup real/área útil injetada, mantendo asserções. Falha intermediária de compatibilidade do teste de Start foi resolvida limpando imediatamente apenas após confirmação de término. Nenhum teste removido, critério relaxado ou prazo aumentado para ocultar falha.

[GENERAL_CHECKUP.md](docs/GENERAL_CHECKUP.md) separa causas corrigidas e limitações. Chamada nativa síncrona bloqueada não é cancelável por WaitAsync: pode manter worker/recurso pendente até retornar. Deadline limita a espera gerenciada, não é prova de que hardware parou. Falha de Dispose após completion é reportada, não repetida cegamente. Em falha terminal não recuperável, recursos não confirmados ficam preservados; não há promessa de recuperação automática. Não houve falha provocada em hardware nem encerramento forçado do aplicativo.

Comandos: `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all`, builds Debug/Release --no-restore; testes direcionados --filter-class '*Checkup*' e classes existentes pertinentes. Regressões pelo script Test-WithWatchdog existente, Debug/seed20261114 e Release/seed20261113, TimeoutSeconds180/MTP165. Para iniciar a UI pela fonte: `dotnet run --project src/WaveStudio.Desktop`. Sem instalação, mudança de ExecutionPolicy, volume/padrão Windows ou gravação/reprodução física automática.

As primeiras suítes com518 casos passaram (Debug59,112 s/Release31,032 s). Na revisão final, mais um teste reproduziu mensagem falsa de “apagado da memória” durante Clear com stream ativo (18/19 passaram na classe antes da correção); agora informa descarte pendente até completion. Após essa mudança, repetição necessária e final com519 aprovados em ambas as configurações,496 regras/simulações/arquivos/STA e23 integrações Windows somente leitura/janelas. Logs finais em artifacts/clock-drift/Debug-20261114-1ebf8f4ac3b04e57b125bae87cab58b9 e Release-20261113-ab46c3f9bf104089a6223be61eda7328; watchdog=False, pipes completos, nenhum runner encerrado. Causa histórica do runner não declarada resolvida.

Atualização remota f400e17 apenas do README incorporada por fast-forward antes da publicação: nota local preservada em stash e reinserida no README novo, restante do trabalho/código intacto. Não houve nova alteração de código após as suítes finais, só documentação e publicação. Requisitos, AGENTS/NEXT_TASK/REVIEW, master/hub, DSP/headroom/FIR e dependências preservados contra a base sincronizada.

Prévia local Release x64 framework-dependent em artifacts/alpha-preview/win-x64/WaveStudio.exe. Publicação em staging-checkup-1-20261114 com --no-restore --self-contained false, DebugType=None, DebugSymbols=false, IncludeSourceRevisionInInformationalVersion=false. Nenhuma instância do proprietário aberta nas conferências; processo próprio5148 com --settings-file exclusivo abriu/respondeu e fechou normalmente/código0, settings de teste salvos, nenhum áudio iniciado. PE AMD64, produto Wave Studio/FileVersion0.1.0.0 e logo/ícone preservados. Revisão visual e testes físicos permanecem pendentes.

| Arquivo publicado | Bytes | SHA-256 |
| --- | --- | --- |
| WaveStudio.exe |303.616|97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285|
| WaveStudio.dll |518.656|5CF268D8CA97440EE6A2ED3728D4099DF4D98492CE4A218070EA2FE266952197|
| WaveStudio.Audio.dll |200.704|CCD1FB262E646F89BE6F83327E211C5D272A8729475460010FA8FA47ABABCB85|

Apphost mantém hash; mudanças estão nas DLLs. Pacote completo com8 arquivos runtime,4 documentos e4 licenças, hashes/manifesto/startup em artifacts/alpha-preview/validation/checkup-1; backup anterior preservado em artifacts/alpha-preview/backup-before-checkup-1-85f2be20cef54f37a67ea28e473706ad antes da promoção. Preferências/arquivos desconhecidos não substituídos. Exige .NET Desktop Runtime10 x64; sem instalador/release pública. Conferência de conteúdo versionável, credenciais/artefatos e diff check antes do commit/push autorizado; sincronização/árvore limpa verificadas após publicação.

Pendências: F2-I6A (ponte pública IAudioClock/HRESULT S_OK/S_FALSE na mesma sessão, preparação sem Start) após revisão; F2-I6B opt-in posterior com guard realmente compartilhado, ownership/quiescência/priming e checkpoint nativos, validação/autorização. Gate/start e watermark atuais são apenas fake, não contrato WASAPI. A01–A13, USB/suspensão/layout e Windows 11x64 seguem pendentes, Windows 10 experimental; causa histórica de intermitência não comprovada. Alpha/Fase 2 não aceitas. Parar após CHECKUP-1, sem Fase 3/efeitos em massa ou release pública.

## Histórico — EQ-PREVIEW-1, EQ audível no diagnóstico curto

14/11/2026: execução/commit/push/republicação autorizados. Main limpa em ad504f8, sincronizada por fast-forward para54d545e e, antes da publicação, para ca6cf8a; remoto Wave-Universal-Studio/main confirmado. AGENTS/NEXT_TASK/REVIEW e requisitos/arquitetura/status consultados; normativos/tarefa/revisão preservados. Escopo pequeno: reutilizar gravação curta, buffer, worker, player e biquads NAudio; não expandir FIR/clocks/watchdog ou integrar F2-I6. Quatro commits de planejamento remoto incorporados sem conflito por fast-forward, com trabalho local preservado em stash e reaplicado; planos de hub/aparência preservados, sem implementação. Código verificado permaneceu igual, sem repetir suítes por mudanças documentais.

| Categoria | Resultado EQ-PREVIEW-1 |
| --- | --- |
| Implementado | GraphicEqualizerSettings/GraphicEqualizer em Audio/Dsp; provider BoundedTestAudio e engine/session existentes integrados; EqualizerPreviewPanel na aba Testar microfone |
| EQ | Dez peaking biquads públicos NAudio.Core 3.1.0 por canal, Q√2, centros31/62/125/250/500 Hz e1/2/4/8/16 kHz; ganhos−15/+15 dB, preamp−30/0 dB, bypass/reset. Sem dependência/projeto novo |
| Segurança | EQ off/bandas/preamp neutros inicialmente, nível digital20%; preamp efetivo desconta soma dos boosts (conservador, pode soar mais baixo), sanitização antes dos filtros, hard clip final contado após nível digital. Não é limiter nem normalização de loudness |
| Formatos | PCM16/float32 mono/estéreo em 44,1/48 kHz; demais taxas mantêm original, EQ desabilitado com motivo. Valida taxa gravada real/Nyquist; canais/estados separados |
| A/B | Original selado imutável em um único clip≤10 s/8 MiB. Filtros novos/snapshot imutável por play, sem cauda adicional. Ajustes congelados durante atividade; sem captura nova, arquivo processado, alocação recorrente ou trabalho pesado na UI |
| Ciclo de vida | Play/stop/clear, ID/revisão/cancelamento e conferência antes de Start preservados; provider invalida antes/depois de copiar. Seleção/desconexão/suspensão/fechamento descartam conforme o diagnóstico existente |
| Testes novos |54:40 respostas centrais ±15 dB/duas taxas (±0,12 dB com headroom),11 regras/provider/ciclo e3 UI. Flat/bypass/preamp/reset, estabilidade, canais, PCM, clipping/sanitização, original, EOF, estado novo, invalidação e abertura cancelada;100 leituras aquecidas sem bytes alocados na thread medida |
| Direcionados |100 aprovados no conjunto EQ+microfone;21 UI/preferências após ajuste de seletores. Nenhum teste novo inicia áudio físico |
| Restore/build |SDK 10.0.404, Git 2.56; restore --locked-mode com NuGetAudit=true/Mode=all aprovado sem alertas; Debug/Release x64 com0 erros/0 avisos. SDK/dependências/locks/branding preservados |
| Regressão final | **495 aprovados por configuração** (441 anteriores+54 novos),0 falhas/ignorados; Debug59,994 s/Release29,826 s; watchdog=False, pipes completos |
| Distribuição | Prévia local Release x64 framework-dependent, com backup e validação própria de abertura/fechamento. Caminho habitual artifacts/alpha-preview/win-x64/WaveStudio.exe; hashes/manifesto abaixo |

Detalhes e roteiro A/B em [EQ_PREVIEW.md](docs/EQ_PREVIEW.md). API pública `BiQuadFilter.PeakingEQ(float,float,float,float)`/`Transform(float)` conferida na fonte 3.1.0 e por compilação/testes, licença MIT existente; sem copiar filtros ou criar DSP de relógios novo. O provider decodifica no span de reprodução, aplica preamp→cascata→nível digital→proteção/PCM; não escreve volume/mute/padrão Windows. PCM16 flat pode diferir do original por até1 LSB na quantização; bytes armazenados do original não mudam. A comparação não é casada automaticamente em loudness.

Ocorrências intermediárias preservadas: duas tentativas direcionadas tiveram99 aprovados/um timeout ao limpar. Diagnóstico mostrou Clear desabilitado enquanto Play já habilitado; uma reordenação experimental revelou a mesma espera incompleta no sentido oposto e foi descartada. Ajustado somente o fixture para aguardar os estados pertinentes juntos, sem retirar asserções ou aumentar prazos. O primeiro Debug completo teve494/495 aprovados: teste de preferências usava último checkbox e passou a ler o novo EQ; corrigido para localizar explicitamente Seguir padrão de captura/reprodução.21 casos de UI/preferências passaram depois. Não foi reproduzida nem declarada resolvida a causa histórica completa do runner; sem expansão de watchdog/produção por hipótese.

Comandos: `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all`; builds Debug/Release --no-restore; testes direcionados `dotnet test --no-build -c Debug -- --filter-class '*EqualizerPreview*' --filter-class '*MicrophoneTest*' --timeout 40s`. Suítes finais pelo watchdog existente, Configuration Debug/Release, Seed20261114/20261113, TimeoutSeconds180 (MTP165); sem alteração no script, política de execução, SO ou instalação. Para iniciar pela fonte: `dotnet run --project src/WaveStudio.Desktop`.

Logs finais em artifacts/clock-drift/Debug-20261114-0f36f5989af549c891a01d818158e894 e Release-20261113-b506a9c6acd4435191b4077c07bceba9:495 aprovados em cada,474 regras/simulações/arquivos/STA e21 integrações Windows existentes,0 falhas/ignorados. Primeiro Debug494/495 em58,471 s permanece no diagnóstico local. Não foi necessário encerrar runner; pipes completos. Nenhuma suíte completa extra foi executada após as duas finais aprovadas.

Republicação local concluída: `dotnet publish src/WaveStudio.Desktop/WaveStudio.Desktop.csproj -c Release --no-restore --self-contained false -p:DebugType=None -p:DebugSymbols=false -p:IncludeSourceRevisionInInformationalVersion=false -o artifacts/alpha-preview/staging-eq-preview-1-20261114`. Nenhuma instância do proprietário aberta nas conferências antes da promoção. Instância própria15480, com --settings-file exclusivo sob validation/eq-preview-1, abriu com título oficial, respondeu e fechou normalmente/código0; configurações de teste salvas e nenhum áudio iniciado. Revisão visual física não realizada. PE AMD64/produto Wave Studio/FileVersion0.1.0.0 e marca incorporada preservados.

Pasta habitual artifacts/alpha-preview/win-x64 atualizada com15 arquivos conhecidos (8 runtime,3 documentos de instrução/avisos e4 licenças), hashes comparados com staging. Backup anterior intacto em artifacts/alpha-preview/backup-before-eq-preview-1-f16ea75bc56c48029ec8af6ced168282; arquivos desconhecidos/preferências não substituídos. Manifesto completo e startup em artifacts/alpha-preview/validation/eq-preview-1, ignorados pelo Git. Requer .NET Desktop Runtime 10 x64; Windows 10 de ensaio continua experimental, sem instalador/release pública.

| Arquivo publicado | Bytes | SHA-256 |
| --- | --- | --- |
| WaveStudio.exe |303.616|97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285|
| WaveStudio.dll |512.000|F549BDD438F5A8CC0CB7F8ED12658FA2F278AA5F5DA80A06A07A325F280D601C|
| WaveStudio.Audio.dll |195.072|4745F089231E2C3893CB315F0585E9139FABD14BE463F0F10EDA7222C672343A|

O apphost/ícone mantém hash; as mudanças estão nas DLLs. Criados 5 arquivos: DSP, painel EQ, duas classes de testes e docs/EQ_PREVIEW.md. Atualizados diagnóstico/fixtures e documentação pertinente; os ajustes de fixtures preservam semântica/asserts. Conferência final do índice inclui diff check, padrões de credenciais e arquivos sensíveis/artefatos; normativos/locks/branding e motor isolado existentes preservados. Commit/push autorizados, com conferência de árvore limpa/sincronizada após publicação.

Pendências: **A13 audível**, layout/DPI e descarte durante troca/USB/suspensão a validar pelo proprietário com fones/nível baixo; não houve reprodução/gravação física automática. A01–A12 e Windows 11 x64 seguem pendentes, Windows 10 experimental. EQ afeta somente a gravação curta; não é EQ global/microfone virtual/efeito no Discord/OBS/jogos. Alpha/Fase 2 não aceitas. Parar após esta entrega; próxima decisão após escuta/revisão e saldo é retomar F2-I6/clock nativo/stream e posteriormente prova externa Fase 3, antes de expandir efeitos/mixer. Não há medição ou promessa de economia percentual de cota.

## Histórico — F2-I5, formatos/relógios/ciclo de vida WASAPI isolados

Implementação, commit/push e republicação local autorizados expressamente. Main inicialmente limpa em1771d39; sincronizada por fast-forward para92fedff, remoto `https://github.com/Wave-Universal-Studio/main.git` confirmado. AGENTS.md, NEXT_TASK.md, REVIEW.md e referências de requisitos, arquitetura, motor, qualidade, clocks, implementação, testes e aceitação consultados. Arquivos normativos/tarefa/revisão preservados contra92fedff. Nenhuma alteração anterior do proprietário foi descartada.

| Categoria | Resultado F2-I5 |
| --- | --- |
| Implementado | Cinco arquivos internos Audio/Engine: WasapiContracts, WasapiEndpointProbe, WasapiClockAdapter, IsolatedTransportContracts e IsolatedTransportSession; sem projeto/pacote novo |
| Consulta real | ID/direção/geração explícitos em MTA, mix/estado/capacidade exata e períodos; modelos gerenciados independentes de COM, wrappers locais descartados. Nenhum Initialize/Start nativo novo ou escrita de níveis/padrões |
| Formatos | Tag/GUID, taxa/canais, container/valid bits, align/bytes/cbSize/máscara; PCM16 LE/float32 LE mono/LR, 48→48/44,1↔48. Rejeita ambiguidades/PCM32/packed24/multicanal, sem fallback/closest ou conversão escondida |
| Evidência nativa | Captura mono48kHz float32/máscara4/align4/192.000 bytes/s; reprodução LR48kHz float32/máscara3/align8/384.000 bytes/s, EXTENSIBLE/cbSize22. Ambos período padrão10 ms/mínimo exclusivo3 ms, capacidade exata. Buffer/latência null; consulta não é negociação efetiva/áudio físico |
| APIs/clock | NAudio 3.1.0 auditado no SHA0aaef29d04bec9567bdf2f669036fabecc33a2e2 e documentação Microsoft. Wrapper GetPosition testa−1, sem preservar S_FALSE+1 de precisão incerta: caminho de clock/stream nativo ativo bloqueado. Alternativas públicas futuras documentadas |
| Adaptação de clock | Position/frequency/unidade/QPC100ns/qualidade/flags explícitos, deltas/remainders UInt128, posição e QPC acima de 2⁵³, overflow/idade/regressões/estagnação. Falha invalida gate imediatamente; não fabrica clock físico com Stopwatch/bytes |
| Sessão | Worker MTA/fila8, estados explícitos, produtor e consumidor separados, borrowed spans, scratch fixo; adaptativo serial no produtor, SPSC real. Gate definitivo por geração antes de Stop/cancel/falha, submissão fake revalida, descarte somente quiescente |
| Recuperação | Início parcial, backend/desconexão/suspensão simulados, callbacks/IDs antigos, filas/stop/cancel em voo e interlock exercitados. Falha retida, recuperação manual com geração nova e descarte/priming contabilizados; não há fallback/restart automático |
| Timeout | Quiescência até1 s; expirado mantém recursos/interlock. DisposeAsync limitado, sem destruir callback em uso; nova tentativa explícita após retorno testada.256 comandos concorrentes contra descarte completam ou rejeitam, sem tarefas órfãs |
| Ocupação/controle | Integral de contadores/eventos por papel com watermark **do fake**, não média de snapshots. Exemplo média 1184; futuro/intervalo não fechado recusados. Drop/underrun real→fronteira→ReprimeRequired, alvo±500 ppm/slew25. Snapshot separa correção solicitada/aplicada |
| Amostras | PCM16↔float32, mono→LR, blocos irregulares, silent/payload vazio, mute default e nível digital0,2; nenhum nível Windows escrito. FIFO48.000 produzidos/lidos, priming/descarte1.024, zero perdas e hash1A1C0D8E5FE4E9FD igual à referência FIR/ganho |
| Drift aplicado |5 s sintéticos com captura+300 ppm:240.063 produzidos/240.000 lidos, zero perdas; alvo−100 ppm e aplicado−97,5 ppm. Prova atuação real do slew no produtor; não afirma convergência longa/hardware, cobertos separadamente pelos ensaios anteriores |
| Restore/build | SDK 10.0.404/Git 2.56; restore locked com NuGetAudit=true/Mode=all aprovado, sem alertas. Debug e Release x64 com0 erros/0 avisos. Dependências/locks/SDK/nominal/adaptativo anterior/branding/Desktop preservados |
| Testes finais | **441 aprovados por configuração**,0 falhas/ignorados:375 anteriores+66 novos;420 regras/simulações/STA e21 integrações Windows somente leitura/janelas. Debug53,850 s; Release25,687 s. Watchdog não precisou encerrar runner, pipes completos |
| Custo callbacks |100 pares aquecidos ×480 frames, FIR/SPSC/codificação/hash;0 bytes alocados na thread medida, Debug77,1036 ms/Release17,6812 ms. Sem asserção de desempenho/deadline físico |
| Prévia | Release x64 framework-dependent republicada localmente em artifacts/alpha-preview/win-x64; startup próprio e fechamento normal código0 com settings exclusivo, metadados/PE/recursos/licenças/hashes conferidos. Sem release pública/instalador |

### Verificações e publicação local

Comandos: `dotnet restore WaveStudio.slnx --locked-mode --force -p:NuGetAudit=true -p:NuGetAuditMode=all`; `dotnet build WaveStudio.slnx -c Debug --no-restore` e Release; testes por `& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 scripts/Test-WithWatchdog.ps1))) -RepositoryRoot (Get-Location).Path -Configuration Debug -Seed 20261114 -TimeoutSeconds 180`, seguido de Release/seed20261113. Prazo MTP165 s, externo180 s inclui pipes. Logs finais Debug-20261114-8e0c3f443d6141759390315bc4d7ee9a e Release-20261113-2c0b3f6e4fd74349ad6c4a90e0e4923f em artifacts/clock-drift; métricas em artifacts/wasapi-backend, sem áudio/IDs nos novos relatórios. Nenhuma instalação, elevação ou alteração de ExecutionPolicy/segurança.

Execuções intermediárias passaram com438/439/440 casos enquanto se ampliava cobertura; últimas441 são após todas as mudanças. Primeira classe de sessão teve20 aprovados/4 falhos por comparar pico de degrau FIR com ganho em regime: corrigida a referência do teste, verificando ringing limitado e ganho estabilizado separadamente, sem alterar FIR/ganho. Assinaturas públicas e asserts assíncronos tiveram erros de compilação intermediários corrigidos; builds finais zerados. Nenhum novo timeout da suíte nesta etapa. Causa histórica completa de runner continua não comprovada, sem apagar diagnóstico anterior.

Publicação: `dotnet publish src/WaveStudio.Desktop/WaveStudio.Desktop.csproj -c Release --no-restore --self-contained false -p:DebugType=None -p:DebugSymbols=false -p:IncludeSourceRevisionInInformationalVersion=false -o artifacts/alpha-preview/staging-f2-i5-20261114`. Apphost AMD64, produto Wave Studio/FileVersion0.1.0.0; ícone/logo incorporados preservados. Oito arquivos de runtime, roteiro/avisos e quatro textos de licença acompanham a prévia; .NET Desktop Runtime 10 x64 exigido, host10.0.15. Nenhuma preferência do proprietário sobrescrita; cópia anterior preservada antes da promoção dos arquivos conhecidos.

Não havia WaveStudio do proprietário aberto nas conferências desta etapa. Primeira instância própria oculta não expôs janela/seletores à sondagem (não prova de defeito da UI); processo21752 foi conferido por caminho/criação/handle e recebeu WM_CLOSE normal, saída0. Computer Use foi tentado com repetição/reset previstos, mas native pipe indisponível; não houve inspeção visual por essa ferramenta. Segunda instância própria visível13220 abriu com título oficial, respondeu, salvou somente settings de teste e fechou por CloseMainWindow/código0. Nenhum processo foi morto ou áudio iniciado. Repetição final própria8820 também abriu/respondeu/fechou com código0. Backup anterior em artifacts/alpha-preview/backup-before-f2-i5-8bb03ca6c19c471e99485eb5ec62619f. Artefatos startup/hidden-close/manifestos em artifacts/alpha-preview/validation/f2-i5. Layout/seletores continuam testados pela suíte STA; revisão visual física permanece pendente.

SHA-256 do pacote: EXE303.616 bytes `97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285`; WaveStudio.dll505.344 bytes `0B3A1E5DD83C68167DB9CA9A47173E6298439B68F6E8340BFC58CF088FF95A1C`; Audio.dll188.928 bytes `D6973148456941670AC838ED799EEAA55F877F6BDA0D6D49222F3092CDA04B65`. EXE/apphost mantém o mesmo recurso; mudanças de código estão nas DLLs. Manifesto inclui dependências/licenças/documentos. Para iniciar pela fonte: `dotnet run --project src/WaveStudio.Desktop`.

### Pendências, limites e próxima revisão

Ver [WASAPI_BACKEND_PLAN](docs/WASAPI_BACKEND_PLAN.md) para APIs/fontes exatas, protocolo temporal fake e bloqueios. Interlock provado somente na simulação: futura integração deverá compartilhar guard com diagnóstico curto antes de abrir qualquer novo stream. Clock ativo exige preservar HRESULT/precisão; mix/capacidade não provam negociação real. Watermark/eventos fechados precisam de solução própria para callbacks nativos; médias não são extrapoladas. Nenhuma latência física foi medida. Ganhos Windows, captura curta, medidores, sessões, preferências e marca existentes permanecem intactos.

Proprietário deve testar A01–A12 do roteiro atualizado: abertura/fechamento com correção de área útil, posição/DPI/rolagem/ícones, volume/mute/balanço e alterações externas em nível baixo, sessões com áudio ativo, USB/troca/desconexão, suspensão/retomada, preferências, descarte/limites A11 e reação/frescor A12. Histórico parcial de sucesso do mic após ajustes próprios não especificados preservado, sem atribuir causa ao problema anterior. Windows 11 x64 permanece sem máquina disponível; Windows 10 experimental. Alpha não declarada aceita.

Novos arquivos: cinco Engine acima, três classes de testes e docs/WASAPI_BACKEND_PLAN.md. Atualizados README, arquitetura, motor, implementação, testes, aceitação, avisos, changelog e este status. Conteúdo versionável revisado para credenciais, arquivos sensíveis/temporários e bin/obj/artifacts; nenhum desses artefatos integra a entrega. git diff --check e git diff --cached --check aprovados; busca de credenciais/artefatos no índice sem ocorrências, normativos/locks/branding/Desktop preservados. Commit/push autorizados, com conferência de main/origin após publicar. Parar após F2-I5 para revisão; não iniciar F2-I6, monitoramento físico, EQ, Fase 3, driver, instalador, redesign ou release pública.

## Histórico — F2-I4, resampling adaptativo e integração de amostras

Implementação, commit e push autorizados. Main inicialmente limpa em 5139c51, atualizada por fast-forward para e04198f e posteriormente bd0ab52 durante o trabalho; remoto Wave-Universal-Studio/main confirmado. O complemento do incidente de inicialização foi lido/incorporado sem conflito ou perda das mudanças locais. AGENTS.md, NEXT_TASK.md, REVIEW.md, requisitos e documentos de arquitetura, motor, qualidade nominal, clocks, implementação, testes, aceitação e propostas futuras consultados. Funcionalidades reais antes do redesign, sem iniciar F2-I5 ou Fase 3.

| Categoria | Resultado F2-I4 |
| --- | --- |
| Implementado | AdaptiveResampler e SampleCursor internos em Audio/Engine, proprietário serial; processamento efetivo de float32, sem backend ou UI novos |
| Formatos/razão | Mono/LR, políticas explícitas, 48→48 e 44,1↔48 kHz; razão saída/entrada nominal×(1+ppm/10⁶), ±500 ppm e slew 25 ppm/s. Demais pares/razões recusados; nominal/identidade exata/96→48 intactos |
| FIR/precisão | 161 taps, banco interpolado de 257 fases incluindo extremidade, cutoff no pior downsample, normalização DC; índice inteiro + fração Q48, carry checked. Fase/histórico preservados em comandos |
| Segurança/ciclo | Geração em comandos/Process/Flush, Stop recusa entrega, reset exige geração nova. ReprimeRequired interrompe ambos os estágios do ensaio; estimativa/comando anterior não reutilizados |
| Streaming | Consumo parcial/zero-space/saída unitária, somente prefixo aceito mapeado/sanitizado; EOF congela razão corrente e termina pela posição/cauda Q48, padding separado de entrada real |
| Proteção | Contadores próprios de não finitos de entrada, proteção de saída, saturação representável e clipping final; sem recontar sufixo e sem confundir clipping posterior zero com total da cadeia |
| Qualidade | Na grade pré-definida: erro útil máximo 0,00027030 dB; ripple 0,00027031 dB; pior alias -86,14299 dB; imagem medida -123,90210 dB; DC 5,96046e-8. Metas atendidas |
| Transições | Referência analítica de fase variável em 1/8/16 kHz, três cruzamentos de seam por tom; pior resíduo -103,20046 dB e pico 2,30580e-6, abaixo das metas -70 dB / 0,0002 |
| Integração sintética | Gerador → ChannelAdapter/FIR → SPSC original → memória, hashes FIFO de amostras aceitas/lidas, clocks independentes/jitter, controle ligado/desligado. Quatro pares / oito caminhos / 540 s simulados, sem arquivo de áudio |
| Resultados de fila | Zero perdas com controle; sem controle 656 underruns/drops em cada 48→48 ±300 de 100 s. 44,1↔48 ±100 por 35 s mede convergência/ocupação, sem esgotar orçamento sem controle; os 900 s anteriores continuam na regressão |
| Convergência/atraso | 23,9–73,3 s simulados; erro final até 11,58648 ppm no recorte de amostras. Atraso FIR 1,666667/1,814059 ms conforme entrada, alvo fila 21,333 ms a 48 kHz; não é latência física |
| Estimador | Janela longa inválida não confirma baseline curta, invalida Latest e exige reset explícito. Dois testes reproduzem desvio cumulativo e retomada com/sem estimativa prévia |
| UI/estabilidade | Corrigida exceção comprovada de área útil inválida; lista filtrada e posição salva preservada se nenhuma área válida. Fixture STA usa Application.Run/esperas assíncronas após timeout real pós-Disposed; causa completa histórica ainda pendente |
| Watchdog | Coleta de pipes limitada, nonce/parentesco/criação/handle próprios e orçamento total. Probes de timeout e pai encerrado com stdout herdado exercitados; filho confirmado encerrado, conhost sem nonce recusado |
| Restore/build | SDK 10.0.404; restore locked com NuGetAudit=true/Mode=all aprovado sem alertas. Debug/Release x64 com zero erros/avisos |
| Testes finais | 375 aprovados por configuração, zero falhas/ignorados (336 anteriores + 39 novos): 356 regras/simulações/arquivos/STA/sintéticos e 19 integrações Windows de leitura/janelas. Debug 50,628 s; Release 23,694 s |
| Memória/custo | Payload do adaptativo estéreo 167.564 bytes; com fila/scratch 188.044. Zero bytes alocados na thread medida após aquecimento; mapeados 8.192 frames tanto com saída 512 quanto um frame |
| Preservação | Fluxos de áudio, controles, marca/recursos, SDK/dependências/locks e executável publicado preservados. MainForm recebeu apenas correção de inicialização/posição comprovadamente necessária; sem redesign |
| Próximo passo | Revisão F2-I4 e pendências físicas; proposta F2-I5/backend WASAPI isolado com negociação/ownership/clocks/interlock. Não iniciado |

### Evidências e verificações F2-I4

- Criados AdaptiveResampler.cs, ClockEstimatorWindowTests.cs (dois casos), AdaptiveResamplerTests.cs (nove), AdaptiveResamplingQualityTests.cs (16), AdaptiveIntegrationTests.cs (cinco), scripts/Test-Watchdog.ps1 e docs/ADAPTIVE_RESAMPLING_QUALITY.md. PreferencesUiTests recebeu sete casos de áreas/geometria e fixture assíncrono (13 no total), incorporando bd0ab52. Atualizados AudioClock.cs, watchdog, MainForm/MainForm.Preferences e documentação. Nenhum projeto da solução ou pacote novo; o probe auxiliar é gerado/compilado apenas sob artifacts, ignorado.
- Janela cumulativa reproduzida a 8 kHz: 805 frames/100 ms plausíveis localmente e 8.050/1 s implausíveis globalmente. O evento rejeitado não renova Latest/nenhuma baseline; RequiresReset persiste. Gerações do estimador são imutáveis; o dono refaz/limpa estimador/controlador na troca e não associa ClockEstimate isolado a outro destino. O adaptativo e a SPSC são parados em ReprimeRequired; recuperação registra 256 frames antigos descartados e geração 2, rejeitando chamadas antigas antes de processar novas amostras.
- Qualidade medida depois das metas, sem relaxá-las. Nove configurações estáticas, três impulsos/canais e três transições; índices acima de 2⁵³/milhão de avanços Q48/overflow, EOF e consumo parcial. Todas as amostras de integração finitas, não nulas e com hash FIFO consistente. Banco/históricos por canal, pior razão de downsample e precisão/EOF descritos em docs/ADAPTIVE_RESAMPLING_QUALITY.md. O adaptativo é infraestrutura de amostras, não EQ, ganho do Windows, driver ou sincronização física validada.
- Custo final aquecido, 8.192 mono→estéreo 44,1→48 com slew ativo e flush/hash: 9.091 saídas, hash 22140796100654659. Debug saída 512: 12,2518 ms / 742.013,42 frames/s; saída um: 13,2498 ms / 686.123,56. Release: 2,5900 ms / 3.510.038,61 e 6,5401 ms / 1.390.039,91. Zero alocação na thread medida em ambos, incluindo somente processamento válido após preparação. Não inclui construção/erros/runtime inteiro nem promete deadline. O nominal anterior mantém seu custo conhecido de remapeamento; só o caminho adaptativo novo resolve esse custo. Otimização compartilha coeficiente/índice por tap estéreo, mantendo hash e metas. Sem ela, os cinco testes isolados de integração Debug levaram 98,708 s; dados finais são posteriores à otimização.
- Oito caminhos de amostras totalizam 540 s simulados, cerca de 35,64 s de parede Debug e 12,17 s Release nos trechos medidos de integração final. Clocks +125 ppm comum e ±300/100 relativo, jitter/seed iguais por par; min/max global com controle 687–1.364, média temporal dos últimos cinco segundos 992,18–1.055,23. Priming 1.024 zeros contado, geração real/lidos/drops/fila/drenagem separados; os casos 48→48 cruzam orçamento sem controle, com 656 perdas cada, e controlados não perdem. Recortes de 35 s não esgotam a fila sem controle; regressão preserva os 900 s de contagem para estabilidade longa. Não converter tempo simulado em ensaio de hardware.
- Falha real intermediária: Debug encerrou por exceção não tratada em WindowPlacement.Fit/MainForm na inicialização, depois de 342 aprovados e nenhum caso reportado como falha; suíte incompleta. Áreas úteis com dimensão zero/negativa agora são ignoradas; ausência total conserva bounds/posição persistida e informa indisponibilidade, sem salvá-la sobre o valor anterior. Sete cenários STA cobrem vazio/inválido/misto, coordenadas negativas, dimensão pequena, monitor removido e posição fora da tela/maximizada; seleção habilitada após consulta e preferências/níveis preservados, com arquivo byte a byte nos fallbacks transitórios. O método estrito Fit não foi relaxado. Não foi reproduzida uma alteração de configuração do Windows nem atribuído motivo ao monitor/sessão remota.
- Outra tentativa Release completou todos os 371 com 370 aprovados e um timeout no caso MainWindowReloadsAbsentPreferenceAcrossTwoStartsWithIsolatedStorage: logs mostram segundo FormClosed/Disposed sem aviso de armazenamento, mas não retorno de DoEvents. Fixture convertido para Application.Run próprio e ações/esperas assíncronas, mantendo prazos de 20 s externos à STA, espera interna de 8 s, Join e asserções/avisos. Nove casos isolados Release passaram após essa revisão; suítes finais completas acima passaram. Causa completa histórica não comprovada; não afirmar que guardas de área, modal ou novo loop explicam todos os bloqueios. Nenhum teste foi desabilitado ou marcado como falso sucesso.
- Após incorporar bd0ab52, a tentativa ampliada de 13 casos teve seis aprovados/sete falhos por asserção de painel habilitado antes de terminar a consulta assíncrona. Corrigida a sincronização da espera, sem alterar produção ou retirar asserções/prazos; 13 aprovados isolados e suítes finais ampliadas de 375 aprovadas em ambas as configurações. Códigos/tempos dos ensaios anteriores permanecem nos logs locais.
- Watchdog: limite de processo, término e pipes compõem orçamento; tarefa de leitura pendente nunca recebe Result/GetResult. Falha/coleta incompleta é explícita, assim como identidade recusada. Probes próprios compilados de fonte local: timeout do pai+filho retornou 124 em 20,2532 s (orçamento 30); pai encerrado com filho mantendo pipe retornou 125 em 1,9369 s. Filho confirmado morto e coleta completa em ambos; conhost.exe sem nonce foi recusado e não morto. Códigos não representam falha da suíte xUnit: são resultado esperado do ensaio de watchdog. Não houve kill de runner nas suítes finais nem do Wave Studio do proprietário. Fontes/projetos desconhecidos do probe são preservados; nenhum stack dump, instalação, RunAs ou alteração de ExecutionPolicy/segurança.
- Comandos: `dotnet restore WaveStudio.slnx --locked-mode --force -p:NuGetAudit=true -p:NuGetAuditMode=all`; builds Debug/Release --no-restore; testes via `& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 scripts/Test-WithWatchdog.ps1))) -RepositoryRoot (Get-Location).Path -Configuration Debug -Seed 20261114 -TimeoutSeconds 180` e Release/seed20261113. Esse limite deliberado cobre a nova carga de FIR (MTP 165 s, processo até 170 s e reserva de coleta); sem aumento indefinido para esconder falha. Probes por scripts/Test-Watchdog.ps1 com Mode Timeout/Pipe e orçamento 30 s. TESTINGPLATFORM_TELEMETRY_OPTOUT=1 no script. Relatórios numéricos/diagnósticos em artifacts/adaptive-resampling e artifacts/clock-drift, ignorados pelo Git; nenhum áudio persistido. Para iniciar a UI existente: `dotnet run --project src/WaveStudio.Desktop`; não há controles de resampler nela.
- AGENTS.md, REQUIREMENTS.md, NEXT_TASK.md e REVIEW.md preservados; conferência de conteúdo versionável, diff/índice e remoto antes do commit/push autorizado. Pacote não republicado; EXE e WaveStudio.dll conferidos com SHA-256 97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285 / E2463B1B7871894F94DDE9FB0FC1DD7E504484CBA23FD765CCF81537D70CD941. bin/obj/artifacts permanecem ignorados. Sem instalador, release pública, driver, redesign, F2-I5 ou Fase 3.
- Republicação da prévia não foi inferida da autorização de código/commit/push: o complemento condiciona substituição a autorização específica e fechamento normal da instância do proprietário. O executável corrigido foi compilado em src/WaveStudio.Desktop/bin/Release/net10.0-windows/WaveStudio.exe com dependências na mesma pasta; abertura/fechamento do MainForm foram exercitados pelos casos STA. Pasta habitual preservada, sem encerrar app do proprietário ou anunciar um pacote substituído.

### Pendências e limites F2-I4

Revisão técnica, causa completa do bloqueio histórico, maior cobertura espectral/temporal, monitoramento e latência física não validados. F2-I5 é apenas contrato de backend: negociação real, ownership MTA/COM/callbacks, unidades e qualidade de relógio/QPC, parada/join/revalidação de alvo antes de enviar, geração/priming explícitos e interlock com teste de microfone. Não presumir que NAudio atual já entrega esse contrato nem usar a última razão limitada após ReprimeRequired. O nominal continua sem corrigir drift; o adaptativo só foi validado sinteticamente no subconjunto documentado.

A01–A12 preservam evidências parciais anteriores e subcasos físicos de áudio ativo, seleção/padrões, sincronização externa, balanço, sessões, USB/desconexão, suspensão/retomada, preferências, captura/reprodução/descarte A11, medidor A12 e DPI/ícones. Não houve nova validação física, Windows 11 segue indisponível para ensaio e Windows 10 é experimental. Alpha não declarada aceita; não liberar backend/monitoramento físico sem revisão e regressão Alpha. Correção de área está nos fontes/builds, com executável publicado anterior preservado. Parar após F2-I4.

## Histórico — F2-I3, relógios independentes e estabilidade de testes

Implementação, commit e push autorizados pelo proprietário. Main limpa inicialmente em c206d8c, sincronizada por fast-forward para e9c045f; remoto https://github.com/Wave-Universal-Studio/main.git confirmado. AGENTS.md, NEXT_TASK.md, REVIEW.md e documentos normativos/de arquitetura, motor, qualidade, implementação, testes, aceitação e propostas futuras consultados. Prioridade: produto funcional antes do redesign. Trabalho salvo no repositório e preservado após o fechamento do VS Code informado durante a execução; a primeira resposta de status distinguiu investigação em andamento de entrega concluída.

| Categoria | Resultado F2-I3 |
| --- | --- |
| Implementado | AudioClock/ClockObservation/ClockEstimate e ClockRatioEstimator em AudioClock.cs; ClockRatioController em arquivo próprio, internos e de proprietário serial |
| Estimativa | Razão de taxas efetivas saída/entrada por deltas coerentes em referência comum, janela ≥1 s e filtro alpha 0,2. Identidade/geração, domínio/unidade, qualidade, regressões de tempo/posição, saltos, idade e descontinuidade tratados |
| Controle | Feed-forward + PI de ocupação média temporal (Kp=0,8; Ki=0,02), atualização 100 ms, filtro 2 s, anti-windup, limite padrão ±500 ppm e slew 25 ppm/s; sem dados suficientes permanece nominal, falha exige reset/repriming explícito |
| Simulação regular | 18 pares com/sem controle, 900 s cada: 48→48, 44,1→48 e 48→44,1, ±50/100/300 ppm, eventos independentes, jitter e offsets. Fila 2.048/alvo 1.024, fase Q32 e contabilidade inteira; priming separado |
| Resultado regular | Zero drop/underrun com controle; sem controle 1.181–12.176 frames perdidos por cenário. Ocupação global 681–1.367, média final 1.023,87–1.024,15; maior erro final 3,006 ppm e convergência em 21,8–76,7 s |
| Falhas/recuperação | +1.500 ppm gera perdas/estados de falha; pausa de 300 ms, nova geração, descarte/priming contados e recuperação a +100 ppm sem novas perdas. Outro cenário rejeita 41 timestamps futuros e exige recuperação por estimativa obsoleta |
| Semântica | Modelo de contagem de frames, sem áudio/amostras. Controlador não aplica razão ao FIR nominal, não corrige drift físico e não autoriza envio a dispositivo |
| Estabilidade UI | Coletor de avisos com asserções em formulários invisíveis, logs de fases, deadline da tarefa fora da STA e Join. Watchdog externo restrito ao runner próprio, sem matar programa do usuário |
| Restore/build | .NET SDK 10.0.404; restore locked com NuGetAudit=true/Mode=all aprovado, sem alertas. Debug/Release x64 com zero erros/avisos |
| Testes finais | 336 aprovados por configuração, zero falhas/ignorados: 313 anteriores + 23 novos, total 324 regras/simulações/arquivos/STA/sintéticos e 12 integrações Windows existentes |
| Custo do modelo | 100.000 observações/atualizações após aquecimento: Debug 39,1464 ms, Release 56,9834 ms; zero bytes alocados na thread medida. Concorrência do runner/JIT torna tempos variáveis; não é comparação de otimização ou deadline |
| Consumo parcial nominal | 8.192 frames consumidos / 9.091 produzidos com saída 512 ou um frame, mesmo hash. 55.559 contra 27.395.275 frames cumulativamente oferecidos/remapeados (~493×), sem perda/duplicação. Debug 13,8946/800,6574 ms; Release 5,2733/35,5514 ms; zero alocação medida em ambos |
| Preservação | Sem mudanças no Desktop, FIR/etapa nominal, captura curta, áudio/padrões/volumes do Windows, SDK/dependências/locks, identidade ou executável publicado |
| Próximo passo | Revisão F2-I3 e investigação residual da intermitência; proposta F2-I4 de resampler de amostras com razão variável, antes de backend WASAPI e integração física. Não iniciada |

### Evidências, diagnóstico e verificações F2-I3

- Criados src/WaveStudio.Audio/Engine/AudioClock.cs e ClockRatioController.cs, tests/WaveStudio.Tests/ClockDriftTests.cs (22 casos), NominalPartialConsumptionTests.cs (um), scripts/Test-WithWatchdog.ps1 e docs/CLOCK_DRIFT_SIMULATION.md. Modificado PreferencesUiTests.cs, mantendo seus seis casos. Atualizados README, CHANGELOG, arquitetura, motor, implementação, plano de testes e este status. Nenhum projeto ou pacote novo; callbacks de áudio existentes intactos.
- Metas, ganhos, janela, limite e slew foram registrados antes de medir. Duas tentativas intermediárias do conjunto de 18 casos falharam no critério de convergência; a coleta de todas as métricas revelou alias da leitura instantânea de ocupação a cada 100 ms em blocos de ~5 ms. Corrigido o modelo para média ponderada pelo tempo, sem relaxar critérios/ganhos. Informa perdas reais de fronteira do intervalo mesmo quando a fila já voltou ao interior. Não atribuir essa falha determinística ao runner intermitente.
- Invariantes verificadas: gerados + priming = consumidos + drop + descarteReset + ocupaçãoFinal, unidade sempre em frames de saída na fila. Falta solicitada é underrun, não consumo inventado. Reset de geração limpa fase fracionária e registra fila descartada; não há alocação/crescimento por evento. Dois cenários adicionais de offset comum 0/−250 ppm e offsets de posição independentes, near-long.MaxValue, timestamps inválidos e razão/slew/anti-windup testados. Prazo de convergência e médias referem-se ao modelo, não a hardware.
- Cenário +1.500 ppm: 16.558 drops antes da recuperação, 2.989 atualizações Saturated/ReprimeRequired (2.868 destas ReprimeRequired). Reset/pausa em t=300 s, 1.807 frames descartados e priming acumulado 2.048; convergência após retomada 23,8 s, erro final 1,372 ppm e zero perdas na recuperação. Cenário de timestamp: 41 rejeições, 15 ReprimeRequired, 1.005 frames descartados no reset/pausa em t=154 s, priming 2.048, erro final −0,182 ppm e zero perdas de fila, inclusive após recuperação. Relatórios determinísticos em artifacts/clock-drift/simulation-*, recovery-* e invalid-clock-recovery-*.json; ignorados e sem áudio.
- Investigação limitada do runner: classe de preferências isolada aprovada (seis), regressão inicial aprovada (313), quatro suítes ampliadas aprovadas (336) em Debug/Release com sementes 20261113/20261114. Debug ampliado seed14: 13,782 s; Debug final seed13: 13,574 s; Release seed14: 10,177 s; Release final seed13: 10,184 s. Nenhum runner incompleto, aviso inesperado ou encerramento pelo watchdog nesta etapa; a ramificação de kill do watchdog não foi exercitada. Não foi provada a causa da parada anterior. Possível aviso modal/DoEvents/teardown é hipótese; logs registram construção, startup, refresh, Close/FormClosed/Disposed, thread/timestamp. Ausência de reprodução nesta amostra não elimina a intermitência. Ferramentas gerenciadas de stack não estavam prontas, nada foi instalado; nenhum stack dump foi obtido. Não há falha crítica de produção comprovada nesta etapa que justificasse alterar a UI.
- Watchdog: prazo externo de 90 s e interno MTP de 80 s; só pode encerrar o dotnet recém-criado e descendentes com parentesco/data de criação reconferidos, nunca o WaveStudio.exe do usuário. Logs stdout/stderr/MTP locais em artifacts/clock-drift. A invocação inicial por -File foi recusada pela ExecutionPolicy vigente; código revisado executado por ScriptBlock local, sem alterar política, sem RunAs e sem instalação. TESTINGPLATFORM_TELEMETRY_OPTOUT=1 aplicado pelo script. Deadline da tarefa STA é 20 s, Join 2 s depois da conclusão; PumpUntil sozinho continua incapaz de abortar callback modal.
- Comandos: `dotnet restore WaveStudio.slnx --locked-mode --force -p:NuGetAudit=true -p:NuGetAuditMode=all`; `dotnet build WaveStudio.slnx -c Debug --no-restore` e Release. Testes por `dotnet test --solution ... --no-build` no script com semente, timeout, diagnóstico e filtro opcional. Reprodução: `& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 scripts/Test-WithWatchdog.ps1))) -RepositoryRoot (Get-Location).Path -Configuration Release -Seed 20261113`. UI existente pode ser iniciada por `dotnet run --project src/WaveStudio.Desktop`; não há controle fictício para a simulação.
- Preservados contra e9c045f AGENTS.md, REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, Desktop, FIR/conversão nominal, SDK/projetos/locks e recursos oficiais. git diff --check aprovado; busca de padrões comuns de credenciais e nomes sensíveis sem ocorrências no conteúdo versionável. bin/obj/artifacts ignorados; logs/relatórios e binários não entram no commit. Remoto reconferido sem divergência, com nova conferência do índice antes do commit e da árvore após o push autorizado. Pacote existente não republicado, hashes EXE/DLL conferidos e preservados: 97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285 e E2463B1B7871894F94DDE9FB0FC1DD7E504484CBA23FD765CCF81537D70CD941. Sem instalador, driver, release pública, Fase 3, EQ ou redesign.

### Pendências e bloqueio de integração física

Causa histórica do runner não resolvida; manter diagnóstico e revisão. Resampler adaptativo de amostras, transições/anti-alias no pior ratio, prefixo/custo parcial e contadores de proteção por estágio estão planejados em docs/CLOCK_DRIFT_SIMULATION.md, não implementados. Backend deverá converter unidades reais de posição/frequência e QPC de 100 ns, validar qualidade e ownership conforme documentação pública antes de qualquer stream. Nenhuma evidência sintética libera monitoramento físico ou integração global.

A01–A12 mantêm evidências anteriores: volume/mute e sucesso relatado de captura/reprodução são parciais. Permanecem subcasos de áudio ativo, sincronização externa, balanço suportado, sessões, USB/troca/desconexão, suspensão/retomada, preferências, descarte/limites A11, reação/frescor A12, ícones/DPI e demais critérios do roteiro. Não foi executada nova regressão física nem medida latência acústica. Windows 11 x64 continua sem ambiente validado; Windows 10 experimental. Alpha não declarada aceita; integração contínua bloqueada até revisão e regressão física. Parar após F2-I3.

## Histórico — F2-I2, canais e resampling nominal implementados

Etapa autorizada para implementação, commit e push. Main inicialmente limpa em b875bec, sincronizada por fast-forward até 2e286cf; remoto https://github.com/Wave-Universal-Studio/main.git confirmado e reconferido sem divergência antes da publicação. AGENTS.md, NEXT_TASK.md, REVIEW.md, requisitos, arquitetura, plano do motor, implementação, testes, aceitação e propostas futuras lidos. Prioridade mantida: funcionalidades reais antes do redesign. Alpha e identidade preservadas; Fase 1 continua em validação física.

| Categoria | Resultado F2-I2 |
| --- | --- |
| Implementado | ChannelAdapter, NominalResampler e NominalConversionStage internos em Audio/Engine; nenhuma dependência nova |
| Canais | Preserve, mono→L/R com duplicação unitária e estéreo→mono com média 0,5L+0,5R. Anti-fase cancela; layouts desconhecidos/multicanal, políticas incompatíveis, frames parciais e overlap rejeitados |
| Resampling | FIR causal sinc/Blackman de 161 taps, fases racionais inteiras e normalização DC; 44,1↔48 e 96→48 kHz. Identidade em 44,1/48/96 kHz conserva valores finitos sem atraso; demais pares recusados |
| Streaming/EOF | Consumo/produção parcial em unidades de entrada/saída, blocos até 4.096/8.192 frames, estado entre chamadas, reset e flush explícito. Com cauda FIR, total = ceil((N+160)×Fout/Fin) para N>0; entrada vazia produz zero |
| Segurança | Não finitos viram zero antes do histórico; proteção final separada limita a [-1,1]. Etapa de proprietário serial, reset somente em quiescência; sem I/O/COM/locks/alocação recorrente no processamento válido |
| Trajetória real dos testes | Mono48→estéreo48, estéreo44,1→estéreo48 e estéreo96→mono48 → fila SPSC original na taxa/layout de saída → proteção/render em memória. Drop/underrun contados em frames de saída |
| Ciclo de vida F2-I1 | 96 corridas limitadas de Stop/cancelamento/Dispose com produção/consumo, mais callbacks float/PCM atrasados e reinício. Nenhuma falha comprovada que exigisse alterar a base; contrato não transacional de operações em voo documentado |
| Restore/build | dotnet restore --locked-mode --force com NuGetAudit=true/NuGetAuditMode=all aprovado, sem alertas de auditoria; builds Debug/Release x64 com zero erros/avisos |
| Testes | 313 aprovados por configuração, zero falhas/ignorados: 283 anteriores + 30 novos; 301 regras/simulações/arquivos/STA/sintéticos e 12 integrações Windows existentes |
| Qualidade | Na grade definida previamente: erro útil máximo 0,00148773 dB, ripple máximo 0,00148773 dB, pior alias -81,11294 dB e imagem medida -123,96323 dB. Metas 0,1/0,2/-60 dB atendidas, com limites de amostragem declarados |
| Memória/custo | Maior payload da etapa 137.856 bytes; ensaio com fila/scratch 150.144. Zero bytes alocados na thread medida após aquecimento. Execuções finais: Release 2.049.540,21 frames de saída/s; Debug 774.671,28 |
| Atraso | FIR: 1,814059 ms (44,1→48), 1,666667 ms (48→44,1), 0,833333 ms (96→48); identidade zero. Não é latência física/acústica |
| Integração | Sem vínculo novo com UI, teste de mic, backend contínuo, monitoramento, drivers ou áudio físico. Volume/mute/padrões do Windows não alterados. Resampling nominal não resolve drift |
| Próximo passo | Revisão F2-I2; proposta F2-I3 de relógios/drift/jitter sintéticos e eventual backend isolado conforme revisão/autorização. Não iniciado |

### Evidências, arquivos e comandos F2-I2

- Três arquivos de produção novos: src/WaveStudio.Audio/Engine/ChannelAdapter.cs, NominalResampler.cs e NominalConversionStage.cs. Três arquivos de testes novos: NominalResamplingTests.cs (23 casos), PipelineLifecycleConcurrencyTests.cs (cinco casos) e ResamplingQualityTests.cs (dois casos). Total de 30 novos, sem modificar testes anteriores nem o pipeline original.
- Testados DC, impulso, senos, alias/imagem, canais independentes, anti-fase, não finitos e extremos finitos no FIR, limites/overlap, identidade, blocos irregulares, saída de um frame, contagem exata em mais de 88 mil frames por cenário, reset/EOF/flush, FIFO, overflow/underrun e alocação. Fórmula de contagem também validada para 24 horas e overflow rejeitado, sem executar um dia inteiro de áudio. Fase inteira evita deriva numérica nominal; não houve ensaio prolongado de relógios físicos. Novos testes de corrida usam threads de teste e Join com prazo, sem barreiras no callback produtivo; stress não prova todos os interleavings.
- `Produce` já em voo pode publicar/retornar aceitação após Stop/cancelamento; `Render` zera ao observar invalidação na checagem final. Retorno positivo não autoriza envio físico. Backend futuro deverá revalidar ID/geração antes de submeter, interromper callbacks e aguardar quiescência; não se promete revogação de áudio já enviado ao SO. Etapa de conversão tem ownership serial separado dos gates do pipeline.
- Medição após 500 blocos de aquecimento: 2.000 × 256 frames mono de entrada a 44,1 kHz, 512.000 frames reais e 557.279 frames estéreo de saída durante a janela, sem flush. Debug final: parede 719,3748 ms, CPU processo 859,375 ms; Release final: parede 271,9044 ms, CPU processo 375 ms. CPU inclui runtime/runner/JIT. Execução Release anterior mediu cerca de 2,70 milhões de frames/s, mostrando variação entre ensaios. Zero alocação na thread medida não abrange construção/erros/runtime inteiro; throughput não é garantia de deadline. Relatórios numéricos locais ignorados em artifacts/engine-foundation; método, metas e tabelas em docs/RESAMPLING_QUALITY.md.
- Comandos executados: `dotnet restore WaveStudio.slnx --locked-mode --force -p:NuGetAudit=true -p:NuGetAuditMode=all`; `dotnet build WaveStudio.slnx -c Debug --no-restore` e Release; `dotnet test --solution WaveStudio.slnx -c Debug --no-build -- --timeout 90s --long-running 15` e Release, incluindo repetições com `--diagnostic --diagnostic-output-directory ...`. Execuções finais terminaram em 11,821 s Debug e 9,238 s Release; Release final com `--seed 20261113 --xunit-diagnostics on` e TESTINGPLATFORM_TELEMETRY_OPTOUT=1. Nenhum teste novo abre streams ou grava áudio real.
- O runner incompleto se repetiu em duas outras tentativas, encerradas apenas nos processos próprios (Debug/Release), sem tocar na prévia do proprietário. Debug interrompido reportou 304 aprovados, zero falhas de casos e erro de execução por interrupção. Diagnóstico Release registrou 304 Passed e um InProgress: `PreferencesUiTests.MainWindowReloadsAbsentPreferenceAcrossTwoStartsWithIsolatedStorage`, caso preexistente; oito casos ainda não iniciados. Timeout solicitado de 90 s não encerrou efetivamente o runner. Os seis casos da classe passaram isolados e as repetições completas finais passaram. Causa/interleaving indeterminados, sem stack dump ou correção comprovada; intermitência registrada para investigação/revisão antes da integração física. Logs MTP/textuais em artifacts/engine-foundation/diagnostics-* e f2i2-*.txt, ignorados pelo Git. Log também mostrou acesso negado ao sentinel de telemetria do runner no sandbox; não há prova de relação causal. Não foram alterados Desktop, testes antigos, runner ou dependências para contornar o sintoma.
- Atualizados README, CHANGELOG, arquitetura, plano do motor, implementação, testes e este status; criado docs/RESAMPLING_QUALITY.md. Plano futuro inclui descritores de backend/relógio, timestamps/QPC/posição, qualidade do relógio e simulação limitada de drift/jitter/ocupação, sem implementar razão adaptativa. Não houve avanço à F2-I3, Fase 3, EQ, efeitos, redesign, instalador ou release pública.
- AGENTS.md, REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, Desktop, recursos oficiais, backends existentes, SDK/build e locks preservados contra 2e286cf. Busca de padrões comuns de credenciais e nomes sensíveis sem ocorrências no conteúdo versionável; bin/obj/artifacts continuam ignorados. `git diff --check` aprovado, com nova conferência do índice antes do commit. Executável publicado preservado byte a byte: WaveStudio.exe SHA-256 97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285; WaveStudio.dll E2463B1B7871894F94DDE9FB0FC1DD7E504484CBA23FD765CCF81537D70CD941. Sem republicação desnecessária. Para iniciar a UI existente: `dotnet run --project src/WaveStudio.Desktop`; a etapa nova é exercitada pelos testes, sem controle fictício na janela.

### Testes físicos e limites ainda pendentes

A01–A12 mantêm o histórico em docs/ALPHA_ACCEPTANCE.md: volume/mute e sucesso relatado de gravação/reprodução são evidências parciais, não aprovação integral. Permanecem a completar os casos/subcasos de seleção e sincronização externa, balanço quando suportado, sessões, dispositivos USB/desconexão, suspensão/retomada, preferências, captura/reprodução A11, medidor A12 e conferência visual/ícone/DPI conforme roteiro. Não houve regressão física nova nesta etapa. Windows 11 x64 continua sem ambiente validado; Windows 10 é experimental. Latência acústica, clocks independentes, drift real, streams contínuos e integração externa não foram testados. Antes de integrar o motor em monitoramento, obter revisão/autorização e evidência da Alpha; a prova externa Fase 3 continua condicionando efeitos globais. Alpha não declarada aceita.

## Histórico — F2-I1, fundação sintética isolada implementada

Autorizada implementação, commit e push. Main inicialmente limpa em ace3e1e, atualizada por fast-forward até 189f2e1; remoto Wave-Universal-Studio/main confirmado. AGENTS.md, NEXT_TASK.md, REVIEW.md, requisitos, arquitetura, planos, status, propostas futuras e aceitação lidos. Funcionalidades reais antes do redesign profissional, preservando marca e interface atual. Fase 1 segue em validação física; não foi declarada aceita.

| Categoria | Resultado |
| --- | --- |
| Implementado | Pipeline sintético PCM16/float32 LE → float32 intercalado → buffer circular SPSC → processamento neutro/proteção → memória |
| Formato/negociação | 8–192 kHz, mono/estéreo lógico L/R, frames completos. Taxa/layout diferentes rejeitados; nenhuma presunção de relógios físicos iguais |
| Buffer | Pré-alocado, potência de dois até 65.536 frames; overflow descarta sufixo mais novo, underrun preenche zeros. Cursores uint com rollover, contadores e snapshots limitados |
| Segurança | NaN/Infinity → zero, excesso finito limitado a [-1,1], demais amostras preservadas. Proteção não equivale a efeito/limiter de lookahead; não há ganho nem bypass fictício |
| Ciclo de vida | Stopped/Running/Canceled/Disposed, geração explícita, token e gates por participante, Stop sem espera; reset/descarte somente em quiescência |
| Memória | Até 557.056 bytes de payload por pipeline; bloco até 4.096 frames; sem I/O, COM, espera/locks ou alocação recorrente no caminho estável |
| Integração | Tipos internos em WaveStudio.Audio/Engine, usados pelos testes; nenhum vínculo com Desktop, captura física, monitoramento ou alteração de volume/padrão |
| Restore/build | Restore locked com auditoria aprovado; Debug/Release x64, zero erros e zero avisos |
| Testes | 283 aprovados por configuração, zero falhas/ignorados: regressão dos 252 + 31 novos. 271 regras/simulações/arquivos/STA/sintéticos e 12 integrações Windows existentes |
| Throughput sintético | 51,2 milhões de frames estéreo: Debug 2.590,9652 ms / 19,761 milhões frames/s; Release 1.152,6465 ms / 44,420 milhões frames/s |
| Alocações/CPU | Zero bytes alocados na thread medida após aquecimento. CPU do processo no intervalo: Debug 3.156,25 ms; Release 2.187,50 ms, incluindo runtime/runner/JIT, não exclusiva do motor |
| Latência | Fila teórica testada: 480 frames a 48 kHz = 10 ms; capacidade de 1.024 = 21,333 ms. Não é latência de hardware/acústica |
| Prévia existente | Executável/pasta de publicação preservados; janela do proprietário estava aberta, não foi encerrada ou substituída. Sem republicação nesta etapa isolada |
| Próximo passo | Revisão F2-I1 e validação/regressão física da Alpha antes de integração contínua; backend real/resampling/drift/monitoramento opt-in em incrementos posteriores autorizados |

### Evidências e limites F2-I1

- Produção em Engine/PcmFloatFormat.cs, SpscFrameBuffer.cs, NeutralProcessor.cs e SyntheticAudioPipeline.cs. EngineFoundationTests e EnginePerformanceTests exercitam efetivamente os componentes, sem projetos/contratos vazios. Nenhuma dependência nova. Arquitetura, prova de ordenação/ownership, auditoria NAudio e contratos futuros documentados em docs/AUDIO_ENGINE_PLAN.md.
- Testes de formatos/limites, frames/interleaving, PCM16/float32, wrap/rollover, drop/silêncio, geração/reset/cancelamento/descarte, sanitização/clipping e snapshots. Stress de 100.000 frames em cada um dos quatro cenários concorrentes mono/estéreo, com cursores comuns/próximos do rollover; FIFO e contadores finais preservados. Reentrada de papel recusada, sem bloquear callbacks.
- Ensaio de desempenho: 10.000 blocos de aquecimento, 200.000 blocos medidos de 256 frames, dados/arrays prontos antes da janela. Relatórios numéricos em artifacts/engine-foundation/metrics-Debug.json e metrics-Release.json. I/O do relatório ocorre depois da medição e não contém áudio. Throughput, CPU e bytes medidos não são promessa de deadline, consumo durante jogos ou latência acústica.
- Primeiro runner completo ficou incompleto (276 aprovados, nenhuma falha reportada antes da interrupção); encerrado somente o processo próprio de testes, sem tocar no programa do proprietário. Causa não determinada. Os 31 novos passaram isoladamente; repetição completa com prazo/diagnóstico aprovou 283 em Debug, e Release aprovou 283. Não houve falha crítica reproduzida da base. Avisos xUnit1051 iniciais corrigidos usando o token do contexto nos testes; builds finais sem avisos.
- Comandos: dotnet restore WaveStudio.slnx --locked-mode --force; dotnet build WaveStudio.slnx -c Debug/Release --no-restore; dotnet test --solution WaveStudio.slnx -c Debug/Release --no-build -- --timeout 60s --long-running 10, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1. Nenhum stream físico ou reprodução foi iniciado pelos testes novos.
- A01–A12 seguem conforme evidência anterior: cabeçalho/abas/ícone, leituras pontuais, sucesso relatado da gravação/reprodução e demais subcasos ainda pendentes. Windows 11 permanece sem ambiente validado; Windows 10 experimental. A estratégia global, prova externa Fase 3 e sequência das fases não mudaram. Sem drivers, efeitos, redesign, instalador, release pública ou integração automática de motor.
- Documentos normativos, NEXT_TASK.md/REVIEW.md, Desktop, identidade, backends existentes, SDK/build e locks preservados contra 189f2e1. Busca de padrões comuns de credenciais e nomes sensíveis sem ocorrências no conteúdo versionável; bin/obj/artifacts ignorados. git diff --check aprovado. EXE/DLL da prévia publicada mantêm hashes 97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285 e E2463B1B7871894F94DDE9FB0FC1DD7E504484CBA23FD765CCF81537D70CD941, respectivamente. Commit/push autorizados, com conferência final de árvore limpa e sincronização.

## Histórico — identidade visual oficial incorporada

Etapa autorizada em 13/11/2026, base c28d808, main inicialmente limpa e sincronizada. AGENTS.md, NEXT_TASK.md e REVIEW.md lidos; requisitos/arquitetura preservados. F1-I6-METER já implementada e publicada, com regressão aprovada; seus testes físicos restantes continuam pendentes. Motor de áudio intacto, sem redesign completo ou Fase 2.

- Logo oficial fornecida pelo proprietário: original PNG RGBA 1254 × 1254 em assets/branding/original/WaveStudio.png, preservado byte a byte (SHA-256 A97D343F27C7033BBE06447AC5AED9B20585CD97C97A7EA5A7D7E3D90BCC4E11). Derivados determinísticos sem recorte ou troca de cores, ICO com 16/24/32/48/64/128/256 px e PNGs correspondentes. Guia e geração reproduzível em assets/branding/README.md e scripts/Generate-BrandAssets.ps1. Política de execução do Windows preservada.
- ApplicationIcon Win32 no EXE; ícone e PNG de cabeçalho incorporados à assembly, clones independentes de streams e descarte com a janela. Janela fornece o ícone ao Windows para taskbar/Alt+Tab; atalhos futuros usam EXE, índice 0. Sem arquivos externos de imagem obrigatórios. README exibe a logo relativa no GitHub.
- Produto/título Wave Studio; descrição de gerenciamento de áudio; identificação Wave-Universal-Studio/main e URL; versão 0.1.0-alpha, assembly/arquivo 0.1.0.0. Company identifica a organização do projeto. Nenhuma razão social, licença pública da marca ou assinatura digital foi inventada.
- Restore locked com auditoria aprovado; builds Debug/Release x64 sem erros/avisos. 252 testes aprovados em cada configuração, zero falhas/ignorados: 248 anteriores + quatro de identidade, totalizando 240 regras/simulações/STA/arquivos e 12 integrações existentes. Casos novos verificam original por SHA-256, sete quadros/dimensões do ICO, recursos independentes de streams e metadados.
- Corrigido AssemblyTitle após teste identificar o nome de assembly no título. O carregador legado GDI pode escolher 128 px para pedido de 256; validado diretamente o quadro PNG de 256 no ICO, preservando o carregamento Windows das demais dimensões. Nenhuma resolução foi removida.
- Executável Release republicado na pasta habitual. Extração do ícone pelo Windows e metadados nativos conferidos. Abertura/fechamento normal em configuração isolada, sem acionar áudio. EXE 303.616 bytes, SHA-256 97D1892C98D2DC66A4B8B2F0190AAC1690CD227286B06595FA946DB25E307285; WaveStudio.dll SHA-256 E2463B1B7871894F94DDE9FB0FC1DD7E504484CBA23FD765CCF81537D70CD941. Dependências/licenças preservadas; recursos não exigem arquivos adicionais na pasta de execução.
- Preparados recursos para instalador/download/Releases/atualizações futuros, sem criar instalador, release pública ou publicação externa. Aparência final de janela/cabeçalho em DPI variados, taskbar/Alt+Tab/cache e atalhos futuros aguardam conferência visual humana. Aceitação física A11/A12 e Windows 11 continuam conforme roteiro; Alpha não declarada aceita.
- git diff --check e conferência do índice aprovados; busca de padrões comuns de credenciais sem ocorrências. AGENTS.md, NEXT_TASK.md, REVIEW.md, REQUIREMENTS.md, áudio, SDK/build e locks preservados. PNG/ICO são recursos de fonte autorizados para versionamento; EXE/DLL, configurações e ensaios permanecem ignorados. Remoto reconferido sem divergência antes do commit/push autorizado.

## Histórico — F1-I6-METER implementada, aceitação física pendente

Repositório inicialmente limpo em main, sincronizado por git pull --ff-only de fcb147a até e66bb85. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, NEXT_TASK.md, REVIEW.md, requisitos, arquitetura, planos, aceitação e status lidos. Implementação, commit e push autorizados pelo proprietário. Escopo limitado ao medidor de entrada e suas proteções; não foram repetidos incrementos anteriores nem iniciada a Fase 2.

| Categoria | Resultado desta etapa |
| --- | --- |
| Implementado | Barra horizontal Nível de entrada do microfone na aba Testar microfone, 0–100% de pico real, texto numérico/estado acessível e explicação dos limites de amplitude |
| Reutilização | Mesmo AudioWatchWorker e PeakReading existentes, timer de 100 ms, fontes de pico reutilizadas; barra de Dispositivos preservada |
| Visibilidade | Medição ativa nas abas Dispositivos/Testar microfone, pausada nas demais e minimizada; ativação inicial conferida no Shown |
| Proteções | ID/revisão completos, frescor de dois segundos, valores inválidos rejeitados e limpeza por troca/recuperação/desconexão/retomada/encerramento; pausa invalida leitura em andamento |
| Segurança/privacidade | Sem novo worker/stream de captura, gravação automática, reprodução ou monitoramento automático; nenhum ajuste de volume/mute/ganho/formato/padrões ou privacidade do Windows |
| Dependências | Nenhuma nova; APIs nativas e implementação CoreAudioWatchBackend preservadas, versões/SDK/lock files intactos |
| Restore/build | Restore locked com auditoria aprovado. Debug/Release x64 sem erros e sem avisos |
| Testes | 248 aprovados em cada configuração, zero falhas/ignorados: regressão dos 228 + 20 novos. 236 regras/simulações/arquivos/STA e 12 integrações Windows de leitura/execução isolada |
| Natureza da evidência | Apresentação/visibilidade, geração, atraso, desconexão simulada, descarte e ausência de nova captura automática. Nenhum stream de microfone real ou playback de teste acionado na automação |
| Publicação | Novo Release AMD64 framework-dependent em artifacts/alpha-preview/win-x64/WaveStudio.exe, requer .NET Desktop Runtime 10 x64; proprietário confirmou fechamento normal antes de substituir |
| Evidência física | Complemento remoto ed19c6e: proprietário relatou que gravação/reprodução voltou a funcionar após ajustes próprios não especificados. Sucesso relatado da função principal A11; origem do problema anterior não determinada |
| Pendências | A11 integral: descarte, limite, mute, troca/desconexão e recuperação. A12 comparação do medidor de fala/silêncio/ruído e comportamento durante gravação explícita; layout/DPI, demais casos completos da Alpha e Windows 11 pendentes |
| Próximo passo | Proprietário testar a nova prévia e relatar A11/A12 por dispositivo/sistema. Windows 10 experimental; aguardar revisão técnica e aprovação física explícita da Alpha |

### Verificações da F1-I6-METER

- dotnet restore WaveStudio.slnx --locked-mode --force aprovado com acesso ao NuGet e auditoria, sem alterar versões. dotnet build WaveStudio.slnx -c Debug/Release --no-restore e dotnet test --solution WaveStudio.slnx -c Debug/Release --no-build aprovados, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Produção nova em MicrophoneInputMeter.cs; composição em MainForm/MicrophoneTestPanel; PeakReading.Stale e invalidação da época em EnableMetering. Nenhuma modificação no backend Core Audio nativo nem no componente de gravação/reprodução. Volume digital do teste continua inicialmente em 20%, separado da amplitude de entrada.
- Testes novos em MicrophoneMeterTests e MeterVisibilityTests, mais dois casos em MicrophoneTestUiTests. A janela integrada utiliza worker/microfone simulados, catálogo/controles existentes em leitura e armazenamento isolado. Um teste inicialmente identificou a medição desabilitada na primeira aba antes da seleção inicial estar disponível; corrigida a atualização no Shown e aprovadas as suítes finais.
- A barra recebe somente a seleção vigente, com timestamp/frescor conferidos; troca/recuperação limpa antes da primeira leitura nova. Callback de ciclo de vida só sinaliza flag atômica, e a UI apresenta indisponibilidade até novo vínculo. Valor zero válido não é usado para representar erro/atraso. Textos numéricos/acessíveis dispensam interpretação apenas por cor.
- O relato inicial A11 incluiu zumbido sem voz no Wave/Windows, reação posterior no Windows e troca USB-C/C por C/A sem resolver o problema informado. No complemento ed19c6e, incorporado por fast-forward durante a conferência final, o proprietário relatou sucesso após ajustes próprios não especificados. Registrado sucesso da função principal de gravação/reprodução, preservando o histórico e os subcasos pendentes. Nenhuma causa foi atribuída ao cabo, hardware, configuração específica ou código. A amplitude não identifica fala nem substitui verificação audível. Drivers que só informem nível com captura ativa exigem conferência explícita por Gravar teste; A12 permanece pendente.
- Publicação local gerada com DebugType=None, DebugSymbols=false e IncludeSourceRevisionInInformationalVersion=false. WaveStudio.dll com 124.928 bytes, SHA-256 068B5EBB063EC80EFBA2EF02F6C94A1EAF1100E25D7925BFB4D79EC41FEB3E76; WaveStudio.Audio.dll com 100.864 bytes, SHA-256 68350948F73E0AB14F983DFDBD6BD10C03177CEA8580AC0BEC480346830C5F5F. O apphost pode manter o hash anterior; usar a pasta completa. Manifesto em artifacts/alpha-preview/validation/meter-package-manifest.json, fora do Git.
- Pacote conferido com 14 arquivos, PE AMD64 0x8664, dependências e quatro avisos/licenças preservados, sem símbolos, testes, configurações pessoais ou áudio. Executável publicado iniciou com título esperado e configuração isolada (PID próprio 11532), fechou normalmente por CloseMainWindow/WaitForExit e criou somente settings.json/lock, com zero preferências de níveis. Nenhuma gravação/reprodução foi acionada; nenhum processo do proprietário foi encerrado. Inspeção visual completa e resposta física continuam pendentes.
- AGENTS.md, REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, FUTURE_AUDIO_FEATURES.md, SDK/build, lock files e backends nativos preservados em relação ao remoto, incluindo o complemento de revisão ed19c6e recebido ao final. Busca por padrões comuns de credenciais e nomes sensíveis sem ocorrências no conteúdo versionável; bin/obj/artifacts ignorados. git diff --check aprovado. Commit/push normais autorizados para esta etapa, com conferência de sincronização e árvore limpa após publicação.

## Histórico — F1-I6-MIC implementada, aceitação física pendente

Repositório inicialmente limpo em main, atualizado por git pull --ff-only de 4c05092 para 91552b0. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, NEXT_TASK.md, REVIEW.md, requisitos, arquitetura, planos, aceitação, propostas futuras e status consultados. Implementação, commit e push autorizados pelo proprietário. Nenhum incremento anterior foi reimplementado; Fase 2 não iniciada.

| Categoria | Resultado desta etapa |
| --- | --- |
| Implementado | Aba Testar microfone: Gravar teste, Parar, Ouvir gravação, Parar reprodução e Limpar teste, com estados, tempo e mensagens em pt-BR |
| Captura/reprodução | WASAPI compartilhado nos IDs selecionados, sem fallback do diagnóstico, sem simultaneidade e sem início automático |
| Segurança/privacidade | Trecho em memória até 10 s / 8 MiB, sem arquivo/envio/persistência; nível digital de reprodução inicial 20%, máximo 100%; volumes/mute/padrões do Windows preservados |
| Ciclo de vida | Worker MTA, snapshots sem COM, revisão/cancelamento antes/depois de abrir streams, callbacks limitados e descarte em troca/atualização, desconexão, suspensão e fechamento |
| Formatos | Mono/estéreo, PCM 16 ou float 32, mix de 8–96 kHz; conversão compartilhada do Windows na saída, com falha compreensível quando recusada |
| Dependências | NAudio.Wasapi 3.1.0 existente, APIs públicas/builder e ownership conferidos na tag; nenhuma dependência nova ou mudança de lock |
| Compilado/testado | Restore locked com auditoria. Debug e Release x64 sem erros/avisos; 228 aprovados por configuração, zero falhas/ignorados (184 anteriores + 44 novos) |
| Natureza dos testes | 217 regras/simulações/arquivos/STA e 11 integrações Windows existentes de leitura/execução isolada. Nenhuma captura real ou playback físico automatizado |
| Execução | Novo pacote iniciou com título esperado, fechou normalmente e criou somente settings.json/lock em diretório isolado, sem preferências de níveis |
| Pacote atualizado | Release AMD64 framework-dependent em artifacts/alpha-preview/win-x64/WaveStudio.exe, com dependências e licenças; requer .NET Desktop Runtime 10 x64 |
| Pasta habitual | Pacote F1-I6-MIC copiado da preparação após fechamento normal confirmado pelo proprietário; 14 arquivos conferidos por SHA-256, sem encerrar processo do usuário |
| Evidência física já recebida | Proprietário confirmou volume/mute do microfone. Captura mono (1 canal), balanço corretamente desabilitado; isto não aprova integralmente A03 |
| Pendências | A11 gravar/ouvir/descarte, negociação real, qualidade, mute/permissões, USB/troca/suspensão/fechamento durante atividade e layout. A01–A10 seguem pendentes de resultados completos; Windows 11 sem ambiente |
| Próximo passo | Proprietário testar o novo executável e relatar A11 e demais casos; aguardar revisão/aceitação explícita. Windows 10 experimental; Fase 1 continua em validação |

### Verificações da F1-I6-MIC

- dotnet restore WaveStudio.slnx --locked-mode --force aprovado com auditoria após permitir acesso ao NuGet. A tentativa restrita retornou três NU1900, resolvidos na repetição sem alterar versões. Builds Debug/Release --no-restore e dotnet test --solution WaveStudio.slnx em ambas as configurações aprovados, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- MicrophoneTestModels, BoundedTestAudio, MicrophoneTestEngine, MicrophoneTestSession e CoreAudioMicrophoneTestBackend em Audio; MicrophoneTestPanel e integração mínima em MainForm; testes MicrophoneTestTests e MicrophoneTestUiTests. Estado inicial sem streams, nenhum acesso a SettingsStore/setters de volume no diagnóstico e nenhum novo controle DSP/driver.
- Os 44 casos novos cobrem estados, limites, frames, formatos/mono, atenuação PCM/float e valores inválidos, ausência de execução automática, alvos exatos, erros, parada pendente, cancelamento de abertura, callbacks obsoletos, desconexão/falha, conclusão/watchdog, fechamento idempotente e botões em STA. Escritas de áudio e captura/reprodução desses casos são simuladas.
- Os ensaios publicados visíveis usaram PIDs próprios 20788 e 9856 (pacote final), com configuração isolada. Início, título, CloseMainWindow, WaitForExit e arquivo v1 confirmados; zero preferências de níveis salvas. Uma tentativa anterior oculta não expôs janela para fechamento normal; somente o processo próprio de ensaio 15212 foi encerrado. O PID 22460 da prévia anterior do proprietário foi preservado. Nenhuma captura/reprodução real foi iniciada. Inspeção visual e resultado audível continuam pendentes.
- README, arquitetura, plano, testes, changelog e aceitação A11 atualizados. AGENTS.md, REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, FUTURE_AUDIO_FEATURES.md, SDK/build e lock files preservados. Busca de padrões comuns de credenciais e inventário de nomes sensíveis sem correspondências no conteúdo versionável. Nenhum binário/artefato de compilação preparado para commit. git diff --check e git diff --cached --check aprovados. Remoto reconferido por fetch, sem divergência antes do commit autorizado; publicação por push normal e conferência final de main/origin/main previstas na entrega.
- Pacote final com 14 arquivos, apphost PE AMD64 0x8664 e licenças preservadas. O apphost WaveStudio.exe pode manter o mesmo SHA-256 entre builds: seu destino gerenciado é constante. O pacote inteiro é necessário; hashes das DLLs e manifesto local mic-package-manifest.json em artifacts/alpha-preview/validation identificam a implementação atualizada. WaveStudio.Audio.dll SHA-256 B07AA9836370D74CB5256F25C144D20270DEF18BC6ADAF49FF1113DCEE65A05D. Áudio de teste não acompanha o pacote ou o Git.
- Após a publicação 3794d4a, o proprietário confirmou o fechamento normal da prévia anterior. Ausência de processos WaveStudio conferida, pacote copiado para artifacts/alpha-preview/win-x64 e 14 arquivos comparados por SHA-256, sem divergências. Executável na pasta habitual iniciou com título esperado e configuração isolada, PID próprio 10044, e encerrou normalmente por CloseMainWindow/WaitForExit. Criou somente settings.json/lock, com zero preferências de níveis. Nenhuma captura/reprodução foi acionada. Código, testes, dependências e binários preservados em relação à implementação já validada; alteração complementar somente documental, com git diff --check aprovado. A11 continua pendente.

### Testes físicos ainda pendentes

Usar o novo A11 em docs/ALPHA_ACCEPTANCE.md com headset, volume baixo e níveis/padrões anotados. Conferir gravação explícita, fala, parada automática/manual, nenhuma reprodução até clicar, saída correta, nível digital independente, stop e limpar. Repetir com mono, mute/permissão negada, formatos fora do subconjunto, troca rápida, desconexão, suspensão e fechamento, sem retomada automática. Não foi emitida evidência audível nesta etapa. O relato anterior de volume/mute de captura permanece parcial. Windows 11 x64 prioritário e indisponível; Windows 10 disponível permanece experimental. A aceitação da Alpha não foi concluída.

## Histórico — F1-I6, checkpoint técnico com validação pendente

Repositório inicialmente limpo em main, sincronizado por fast-forward até 8d0e688. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, requisitos, arquitetura, plano, testes, status, NEXT_TASK.md, revisão F1-I5 e FUTURE_AUDIO_FEATURES.md lidos. O proprietário autorizou implementação, commit e push desta etapa. Os incrementos anteriores permaneceram como base, com regressão obrigatória.

| Categoria | Resultado atual |
| --- | --- |
| Correções da revisão | Arquivo excessivo protegido, falhas previsíveis de gravação observadas, seleção bloqueada até a carga, valores indisponíveis tratados e balanço final confirmado após todos os comandos |
| Medidores de sessão | Pico real por instância a 1 Hz no mixer, com silêncio/pausa/atraso/indisponibilidade e geração. Consulta média de 1,68 ms em 100 leituras com quatro sessões reais |
| Compilado | Debug e Release x64, zero erros e zero avisos |
| Testado automaticamente | 184 aprovados em cada configuração, zero falhas e zero ignorados, incluindo os 174 anteriores. 173 casos de regras/simulações/arquivos/STA e 11 integrações Windows |
| Publicação local | artifacts/alpha-preview/win-x64/WaveStudio.exe, PE AMD64 0x8664, framework-dependent, requer .NET Desktop Runtime 10 x64 |
| Execução publicada | Processo iniciado com configuração temporária própria, ativo por 600,34 s, encerramento normal confirmado e configuração criada no fechamento |
| Recursos observados | 61 amostras. CPU equivalente a 0,187% de um núcleo incluindo início. Após 60 s, working set 63,12–72,24 MiB e handles 362–371, final 362 |
| Automação visual | Bloqueada por pipe nativo indisponível. Inspeção visual, interação e recuperação física aguardam proprietário |
| Aceitação física | Proprietário confirmou que testará após disponibilização do executável, já gerado. A01–A10 de docs/ALPHA_ACCEPTANCE.md aguardam relato |
| Compatibilidade | Proprietário confirmou disponibilidade apenas de Windows 10. Windows 10 Pro 22H2 build 19045.6466 experimental. Windows 11 x64 aguardando ambiente/ensaio |
| Limitação funcional | Preferências duráveis de sessões aguardam identidade inequívoca. Amostragem de pico de sessão a 1 Hz pode perder eventos breves |
| Próximo passo | Receber evidências do proprietário, corrigir falhas reais e obter aprovação explícita da Alpha. Fase 1 continua em validação |

### Verificações e decisões da F1-I6

- SettingsStore agora diferencia Oversized de Corrupt, verifica tamanho antes/depois da leitura limitada e bloqueia save/reset/recuperação automática de arquivo excessivo. Crescimento concorrente invalida baseline e preserva principal/backup, com temporário próprio removido. Modelo inválido retorna mensagem e mantém arquivo válido. DrainSettingsSavesAsync e fechamento tratam validação/I/O previsíveis.
- Seletores e handlers aguardam a carga inicial. EndpointPreference.FromSnapshot preserva ausência de controles com erro, evita preferência utilizável de balanço em zero e recusa snapshot totalmente indisponível. Restauração confirma canais finais após unmute, usando tolerância de 2% da base esperada. Zero é informado como não observável. A prévia avisa sobre áudio audível ao desmutar.
- Picos por sessão foram implementados com escopo moderado, aproveitando a reconciliação existente de 1 s. Objetos são criados, consultados e descartados na mesma operação MTA. Medidor emprestado e volume são descartados antes da sessão proprietária. Snapshots contêm valores e timestamp. A grid mantém chave/foco/geração e informa pausa/atraso. API e propriedade dos wrappers conferidas na tag NAudio 3.1.0. Preferências duráveis de sessão continuam condicionadas a identidade inequívoca.
- A consulta real encontrou quatro sessões inativas, quatro picos disponíveis em zero e zero avisos de enumeração. Cem consultas completas com picos tiveram média de 1,68 ms, e dez consultas de endpoint tiveram média de 1,42 ms. O movimento com fala/música e isolamento audível permanecem pendentes. Frequência da UI de sessão é 1 Hz e de endpoint aproximadamente 10 Hz.
- dotnet restore WaveStudio.slnx --locked-mode aprovado. A primeira execução teve três NU1900 por acesso ao NuGet; repetição --force com acesso à rede concluiu sem avisos de auditoria. Builds Debug/Release e testes nas duas configurações aprovados com 184 testes cada, usando TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Publicação por dotnet publish do Desktop em Release, --no-restore, --self-contained false, DebugType=None, DebugSymbols=false e IncludeSourceRevisionInInformationalVersion=false. SDK Windows x64 10.0.404 e PlatformTarget x64 geraram apphost AMD64 confirmado pelo PE. Saída isolada/ignorada em artifacts/alpha-preview/win-x64, com 14 arquivos e total de 1.673.851 bytes, incluindo documentação/avisos. O executável tem 162.304 bytes e SHA-256 7D4749DB85606830D7851BBBDE70181530BAA5A20592070B37FC5FAF94470F92. Dependências e comandos estão no roteiro de aceitação.
- Ensaio do processo publicado com --settings-file temporário completou 600,34 s e 61 amostras, sem saída prematura. CPU acumulada de 1.125 ms, média equivalente a 0,187% de um núcleo incluindo inicialização e 0,0087% após 60 s. Working set após 60 s de 66.187.264 a 75.751.424 bytes, final 75.460.608; private bytes final 31.727.616; handles 362–371 após 60 s, final 362, e 12 threads finais. Relatório bruto ignorado em artifacts/alpha-preview/validation. A amostra cobre esta duração, host e estado inicial da janela.
- CloseMainWindow/WaitForExit confirmaram encerramento normal do processo publicado. O arquivo temporário foi criado com schemaVersion 1, seleções automáticas independentes, janela salva e zero preferências de níveis. Consulta de eventos Application Error/.NET Runtime encontrou zero correspondências ao WaveStudio.exe no intervalo. A inspeção visual e dos erros recuperáveis apresentados na UI continua pendente.
- Computer Use retornou pipe nativo indisponível. Repetição limitada e reinicialização do cliente falharam, e a superfície unificada confirmou indisponibilidade. A validação interativa/visual foi mantida pendente. Após a solicitação, o proprietário confirmou que testará a prévia pronta e dispõe somente de Windows 10. O roteiro A01–A10 orienta volume baixo, registro/restauração dos níveis/padrões e testes de arquivos isolados.
- Avisos MIT do NAudio e do .NET e textos LICENSE/THIRD-PARTY-NOTICES de System.Numerics.Tensors acompanham a pasta de publicação. Inventário em docs/THIRD_PARTY_NOTICES.md. A licença própria do produto permanece decisão do proprietário. A publicação local é uma prévia para testes. Arquivos binários, configurações pessoais e relatório bruto ficam ignorados pelo Git.
- README, changelog, arquitetura, planos, aceitação e status foram atualizados. git diff --check aprovado. Busca por padrões comuns de credenciais e nomes sensíveis sem correspondências no conteúdo versionável. Bin/obj/artifacts ignorados, links locais válidos e arquivos novos conferidos quanto a espaços finais/conflitos. Pacote conferido com dependências/avisos, excluindo símbolos e binários de testes. AGENTS.md, REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, FUTURE_AUDIO_FEATURES.md, configurações de SDK/build e lock files conferidos contra 8d0e688 e preservados. Publicação autorizada como checkpoint técnico, com conferência da árvore e sincronização após push normal.

### Handoff e bloqueios de aceitação

Roteiro e comando do executável em docs/ALPHA_ACCEPTANCE.md. Os casos A01–A10 aguardam resultado aprovado/falhou/pendente por dispositivo e sistema. A aceitação está bloqueada pela ausência dessas evidências físicas/visuais e de ensaio Windows 11, com relato do proprietário pendente. Os testes automatizados e a coleta de processo compõem o checkpoint técnico. A conclusão da Fase 1 e aceitação final da Alpha exigem evidência e aprovação explícita.

Após a publicação do checkpoint 06d2c3f, o proprietário confirmou que realizará os testes quando o executável estiver pronto e que dispõe somente de Windows 10 neste momento. A prévia local está pronta para iniciar A01/A02. Windows 11 permanece pendente por indisponibilidade de ambiente confirmada. Esta confirmação registra disponibilidade para testar, com resultados físicos ainda aguardados.

## Histórico — F1-I5 executada

Árvore inicialmente limpa, main sincronizada por git pull --ff-only até 4e2d690. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, requisitos, arquitetura, planos, status, NEXT_TASK.md e REVIEW.md lidos. A revisão mais recente é F1-I4 sobre b63ef63. Implementação, commit e push desta etapa autorizados pelo proprietário. F1-I6 e Fase 2 aguardam nova orientação.

| Categoria | Resultado atual |
| --- | --- |
| Implementado | JSON por usuário com schemaVersion 1, seleções/modos independentes, preferência ausente, janela validada, valores opcionais por endpoint, salvar/restaurar/reset/recuperar explícitos |
| Proteção de dados | I/O serializado fora da UI, temporário/flush/substituição com backup, conflito entre instâncias, corrupção e versão incompatível preservadas, arquivo inválido arquivado em recuperação explícita |
| Segurança de áudio | Carga/refresh/retorno consultam níveis atuais. Restauração exige prévia, confirmação e revalidação de ID, geração, disponibilidade, suporte e níveis, com leitura após falha parcial |
| Revisão F1-I4 | Falha terminal/freshness do worker, retomada/ciclo de vida com mesmo ID, recuperação manual, API de preferência por ID, fallback ativo e reconciliação pendente do mixer |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 174 aprovados, zero falhas e zero ignorados. 165 casos de regras/simulações/arquivos/STA e nove integrações Windows, incluindo os 101 anteriores |
| Integração | Dois ciclos de abertura/fechamento da janela em armazenamento isolado, seleção ausente preservada e fechamento mantendo bytes de arquivo protegido. Leituras reais de medidores e inscrições aprovadas |
| Execução/acessibilidade | Executável com arquivo temporário iniciou. Acessibilidade reconheceu Preferências locais, seleção ausente e ações. Captura visual e clique por geometria falharam |
| Limitações | Preferências de sessões aguardam identidade durável inequívoca. Medidores contínuos por sessão aguardam avaliação de ciclo de vida/custo na F1-I6 |
| Testes físicos/visuais | Restauração audível, fala/música, USB/padrões/retomada, dois aplicativos, diálogos, DPI, estabilidade prolongada e Windows 11 pendentes |
| Compatibilidade | Windows 10 Pro 22H2 experimental. Windows 11 x64 prioritário e pendente de validação |
| Próximo passo | Revisão técnica da F1-I5 e orientação para validação da F1-I6 |

### Decisões e verificações da F1-I5

- Configuração padrão em %LOCALAPPDATA%/WaveStudio/settings.json. Caminho injetável e --settings-file absoluto permitem ensaios isolados. As verificações desta etapa usaram arquivos temporários próprios e consultas de áudio. Escritas de áudio automatizadas ocorreram em simulações.
- Seleções são salvas após gesto explícito/modo. Valores de volume/mute/balanço usam Salvar valores atuais. Polling, notificações e medidores atualizam o estado atual. Posição/tamanho são salvos no fechamento com ajuste à área útil na carga. Preferências de ID ausente sobrevivem ao reinício e são exibidas como indisponíveis.
- SettingsStore usa System.Text.Json, schemaVersion 1, validação estrita, UTF-8/BOM, limites de tamanho/estrutura/IDs/valores e defaults seguros em memória. A primeira versão aguarda versões anteriores conhecidas para justificar migração.
- Escrita usa exclusão por handle de settings.json.lock e hash do conteúdo carregado, comparado antes de gravar e imediatamente antes de substituir. Transações locais são serializadas e pedidos da UI são coalescidos. Um conflito bloqueia sobrescrita e orienta reabrir. Temporário exclusivo na mesma pasta, Flush(true), File.Replace com backup válido e remoção do temporário próprio. Recuperação/reset arquiva original inválido antes de substituição. Falhas e cancelamento foram exercitados em diretórios temporários.
- Versão incompatível permanece byte a byte, inclusive em save/reset/close. Corrupção aguarda recuperação/reset explícitos, com backup validado. Principal ausente com backup válido também aguarda escolha. Limite de 1 MiB protege alocação, e arquivos maiores exigem inspeção externa preservando o original.
- EndpointRestorePlan captura prévia e geração. Confirmação mostra ID e níveis, avisa aumento e começa com Não selecionado. Serviço revalida identidade/geração, suporte e mudanças externas. Mute ligado vem primeiro e unmute por último. Falha interrompe sequência e mostra consulta real, com possível aplicação parcial. Chamadas nativas iniciadas podem terminar no alvo original.
- Worker publica falha terminal/indisponibilidade e picos antigos expiram após dois segundos na apresentação. Recover invalida leitura pendente imediatamente. Eventos de ciclo de vida, retomada e manual invalidam gerações/erros mesmo com ID igual. Metadados preservam controles. Atualização manual solicita reconstrução e o retorno do catálogo apenas reconcilia, evitando ciclo. Mixer mantém pedido de leitura recebido durante operação anterior.
- Identidade persistente de sessão foi avaliada. GetSessionIdentifier pode ser compartilhado por instâncias, e a camada atual identifica instâncias efêmeras/PID. Correspondência durável inequívoca permanece pendente. A UI informa indisponibilidade dessa preferência. Medição contínua por sessão segue para avaliação de fontes por instância e orçamento na F1-I6, mantendo a leitura pontual atual.
- dotnet restore WaveStudio.slnx --locked-mode aprovado. dotnet build WaveStudio.slnx -c Debug --no-restore aprovado com zero erros e zero avisos. dotnet test --solution WaveStudio.slnx -c Debug --no-build aprovou 174 testes, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Diagnósticos de desenvolvimento corrigidos incluíram CS8602 no botão de preferência, xUnit1051 por ausência de tokens de cancelamento e CS0411 na interseção de retângulos do teste. Dois testes de status consultavam a janela antes do evento Shown, corrigidos para aguardar inicialização. Suíte final aprovada.
- Integração específica observou 20 snapshots em 2,21 s, picos zero em ambas as direções e inscrição ativa. Consulta média de 0,090 ms, CPU do runner com delta de 46,9 ms e working set de 61.886.464 a 63.168.512 bytes. Quatro sessões reais tinham pico zero na leitura pontual. A amostra descreve esta máquina e período curto.
- Executável iniciou com --settings-file temporário e acessibilidade reconheceu os novos controles e a preferência ausente. A captura retornou window capture timed out e o clique coordinate input geometry is unavailable. Uma tentativa inicial de janela em isolamento de processos ficou inacessível, exigindo encerramento apenas dos processos próprios de teste. A verificação final utilizou janela no desktop e arquivo temporário novo. O encerramento normal gerou JSON com modos automáticos e geometria, e o reinício exibiu Preferências carregadas por acessibilidade. A janela final foi encerrada normalmente. Validação visual e operação interativa completa continuam pendentes.
- Produção nova em EndpointPreferenceRestore.cs, MainForm.Preferences.cs e Desktop/Settings (StudioSettings.cs, WindowPlacement.cs, SettingsStore.cs). Testes novos em SettingsStoreTests, SettingsValidationTests, EndpointPreferenceRestoreTests, RecoveryRegressionTests e PreferencesUiTests, além da regressão em MixerGridTests. Nenhum pacote novo. README, changelog, arquitetura, planos e status atualizados. git diff --check aprovado. Busca por padrões comuns de credenciais, dados sensíveis e artefatos sem correspondências no conteúdo versionável, com bin/obj ignorados e links locais válidos. AGENTS.md, docs/REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, configurações de build/SDK e lock files conferidos contra 4e2d690 e preservados. Publicação autorizada com commit descritivo e push normal, seguida de conferência da árvore limpa e sincronização com origin/main.

### Testes físicos pendentes da F1-I5

Anotar e restaurar os níveis originais do Windows e aplicativos. Testar salvar níveis atuais, reiniciar com seleções/modos preservados, prévia/cancelamento e restauração confirmada em hardware, incluindo mono/suporte ausente, alteração externa, troca/desconexão e falha parcial. Ensaios de corrupção/backup/versão futura/concorrência devem usar arquivo temporário via --settings-file. Conferir USB/padrões/retomada, áudio ativo e dois aplicativos, consumo prolongado, 1366×768, DPI 100/150/200% e Windows 11 x64. Procedimentos em docs/TEST_PLAN.md. A aceitação física da Alpha permanece pendente.

## Histórico — F1-I4 executada

Repositório limpo inicialmente, sincronizado por fetch e fast-forward até b4a6223 em main. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, requisitos, arquitetura, plano, testes, NEXT_TASK.md e REVIEW.md foram lidos. A revisão mais recente é F1-I3. O proprietário autorizou implementação, commit e push da tarefa F1-I4. Persistência da F1-I5 aguarda nova orientação.

| Categoria | Resultado atual |
| --- | --- |
| Implementado | Medidores reais dos endpoints, notificações de dispositivos/padrões, recuperação, seguir padrão por direção e correções do mixer |
| Ciclo de vida | Worker MTA persistente proprietário de fontes e inscrição, snapshots por geração, descarte ordenado e callback breve |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 101 aprovados, zero falhas e zero ignorados, incluindo os 77 anteriores. 95 casos de regras/simulações/grid STA e seis integrações Windows de leitura |
| Integração real | Reprodução e captura com pico zero na amostra, inscrição automática ativa e descarte aprovado. Cinco sessões com medidor disponível e pico zero na leitura pontual |
| Execução | Executável abriu e encerrou normalmente. Acessibilidade reconheceu barras, silêncio, seguir padrão, 67 endpoints e detecção ativa. Captura visual retornou timeout em duas tentativas |
| Medidores por sessão | API avaliada pontualmente. Exibição contínua adiada para avaliação do custo e gerenciamento persistente por instância |
| Testes físicos | Resposta a fala/música, hotplug USB, padrões externos, suspensão/retomada, escrita de controles, dois aplicativos, DPI e estabilidade prolongada pendentes |
| Compatibilidade | Windows 10 Pro 22H2 experimental. Windows 11 x64 ainda pendente |
| Próximo passo | Revisão técnica da F1-I4 e ensaios físicos |

### Decisões e verificações da F1-I4

- Pico consultado por AudioMeterInformation.MasterPeakValue do NAudio.Wasapi 3.1.0. APIs públicas e propriedade de wrappers conferidas na tag e no XML instalado. Percentual e dBFS têm silêncio tratado, rejeição de valores inválidos e estado de indisponibilidade. A leitura é local e consulta medidores publicados pelo Windows.
- Worker de aproximadamente 10 Hz mantém dois medidores abertos, pausa leituras quando ocultos e publica snapshots com IDs/revisão. Simulação de 11 ticks verificou duas aberturas e 22 leituras, com descarte na troca e encerramento na thread proprietária. Filas existentes mantêm objetos próprios e continuam serializando controles e sessões.
- Notificações de adição, remoção, estado, padrão e propriedades relevantes apenas sinalizam flag atômica. Coalescência usa 200 ms de quietude e máximo de 1.000 ms. Falha de inscrição é indicada e ativa fallback de catálogo/registro a cada cinco segundos. PowerModeChanged solicita reconstrução e consulta na retomada.
- Seleção explícita é mantida durante ausência e retorno. Modo inicial e checkboxes acompanham padrão multimídia por direção. Binding de listas preserva o modo automático. Refresh/hotplug/retomada usam leitura dos níveis existentes. Os comandos continuam associados ao alvo e à geração capturados.
- Grid reconciliada por SessionKey, com preservação de linha/célula e edição. Cancelamento, binding, índices e reescolha do mesmo ID são protegidos. Aplicação de comando solicita consulta imediata e mantém erro visível. Falhas de abertura/identidade de sessões isoladas permitem buscar o alvo confirmado. Falhas após confirmação propagam-se.
- Uma amostra específica de 20 snapshots em 2,21 s observou consulta média de 0,083 ms. O processo completo do runner apresentou delta de CPU de 31,2 ms e working set de 59.949.056 a 61.140.992 bytes. Valores descrevem esta máquina e uma amostra curta. A resposta a fala/música continua pendente.
- dotnet restore WaveStudio.slnx --locked-mode aprovado. Durante o desenvolvimento, uma restauração encontrou três NU1900 por restrição de rede na auditoria NuGet. Repetição com acesso à rede passou com auditoria e zero avisos.
- dotnet build WaveStudio.slnx -c Debug --no-restore aprovado com zero erros e zero avisos. dotnet test --solution WaveStudio.slnx -c Debug --no-build aprovou 101 testes, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Testes STA iniciais expuseram edição ainda ativa após CancelEdit e descarte da grid nessa condição. O encerramento e a troca agora finalizam a edição sob proteção de binding. Os três testes da grid passaram na suíte final.
- Arquivos novos de produto são AudioWatchModels.cs, AudioWatchEngine.cs, AudioWatchWorker.cs e CoreAudioWatchBackend.cs. Testes novos em AudioWatchTests.cs, HotplugSelectionTests.cs, MixerGridTests.cs e AudioWatchIntegrationTests.cs, com regressões adicionais em ApplicationSessionTests.cs. Atualizados os painéis, MainForm, seleção e serviço de sessões. Projeto de testes referencia Desktop para regressão STA, e seu lock registra essa referência. Versões NuGet e locks de Audio/Desktop preservados.
- Atualizados README, CHANGELOG, arquitetura, plano de implementação e plano de testes. AGENTS.md, docs/REQUIREMENTS.md, NEXT_TASK.md e REVIEW.md conferidos contra b4a6223 e preservados integralmente. git diff --check aprovado. Busca por padrões comuns de credenciais e nomes sensíveis sem correspondências no conteúdo versionável. bin/obj confirmados como ignorados. Links locais válidos e arquivos novos conferidos quanto a espaços finais e conflitos. Publicação autorizada com commit descritivo e push normal, seguida de conferência da árvore e sincronização com origin/main.

### Testes físicos pendentes da F1-I4

O proprietário deve anotar níveis, mute, balanço e padrões existentes e restaurá-los ao final de ensaios de escrita. Reproduzir áudio e falar por ação própria para conferir movimento e silêncio nas duas barras. Desconectar/reconectar USB com seleção explícita e confirmar retorno ao mesmo ID. Alterar padrões multimídia/comunicações pelo Windows e verificar modo automático independente e marcas reais. Suspender/retomar somente por ação do proprietário. Testar dois aplicativos, foco/edição/Enter/Esc e volume/mute por sessão, arraste/teclado dos controles, encerramento durante atividade e uso prolongado. Conferir 1366×768, DPI 100/150/200% e Windows 11 x64. Procedimento detalhado em docs/TEST_PLAN.md.

## Histórico — F1-I3 executada

NEXT_TASK.md e REVIEW.md foram lidos após sincronização por fetch e fast-forward até 4ab114d. O proprietário autorizou implementação, commit e push desta etapa. A tarefa F1-I3 foi executada, incluindo os ajustes da revisão do Incremento 2. O Incremento 4 aguarda nova orientação.

| Categoria | Resultado atual |
| --- | --- |
| Implementado | Mixer real de sessões de reprodução, volume 0–100%, mute por instância, reconciliação e mudanças externas por polling |
| Identidade | Endpoint + identificador de instância, com revisão/cancelamento por seleção e revalidação imediatamente antes da escrita |
| Revisão do Incremento 2 | Gerações liberadas ao terminar operações, sliders confirmados no fim do gesto, janela com rolagem e limites da área útil, proteção da restauração contra mudanças externas detectáveis |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 77 aprovados, zero falhas e zero ignorados. 73 casos de regras/simulações e 4 integrações Windows de leitura, incluindo regressão dos 56 anteriores |
| Integração real | 4 sessões, todas inativas, zero avisos no endpoint de reprodução padrão. Duas instâncias de LEDKeeper2 com mesmo PID, FaceLift_x64 e Sons do sistema |
| Execução | Executável abriu, abas Dispositivos e Mixer de aplicativos reconhecidas por acessibilidade. Captura visual falhou por timeout |
| Testes físicos/visuais | Escritas, isolamento audível, sincronização externa pela UI, arraste contínuo, teclado/roda do mouse, 1366×768 e DPI elevado pendentes |
| Compatibilidade | Windows 10 Pro 22H2 experimental. Windows 11 x64 ainda pendente |
| Próxima etapa | Revisão técnica da F1-I3 e testes físicos. Incremento 4 pendente |

## Decisões e verificações da F1-I3

- O mixer possui uma aba própria e administra somente sessões do endpoint de reprodução selecionado. PID é metadado auxiliar. Instâncias do mesmo processo e sons do sistema continuam separados. Ausência de sessões é estado válido.
- Operações de sessão e de endpoint compartilham a fila serializada EndpointOperationQueue. Cada geração cancela pedidos antigos e libera seu CancellationTokenSource quando a última operação termina. Testes exercitam 2.000 trocas, incluindo uma operação bloqueada e descarte idempotente.
- Os sliders de endpoint exibem prévia durante Scroll e escrevem ao terminar o gesto de mouse/teclado, roda do mouse ou saída do controle com valor pendente. Atualizações de snapshots ficam protegidas contra gerar comandos. A interação contínua ainda exige conferência física.
- A janela limita tamanho à área útil e oferece rolagem na aba de dispositivos, com tamanho mínimo reduzido. A revisão visual de áreas menores e diferentes DPIs permanece pendente.
- O polling de endpoints permanece em 600 ms. O mixer usa 1.000 ms e suspende consultas periódicas quando a aba está oculta ou em edição. Medição de dez leituras completas observou médias de 1,40 ms no endpoint e 1,57 ms no mixer, nesta máquina. São tempos de parede de uma amostra curta, sem equivalência a orçamento de CPU ou garantia de desempenho. Notificações persistentes e otimização do ciclo COM ficam para o Incremento 4.
- Balanço mantém o limite do nível-base. A restauração compara os canais atuais com os níveis originais/tentados e evita sobrescrever uma alteração externa detectável. Desconexão ou concorrência entre chamadas nativas pode impedir restauração. A UI volta a consultar o estado real. Chamadas nativas já iniciadas podem terminar no alvo original.
- APIs da tag NAudio 3.1.0 e XML do pacote foram conferidas. AudioSessionManager.Sessions, GetSessionInstanceIdentifier, GetProcessID, IsSystemSoundsSession, State e SimpleAudioVolume são usados com descarte de cada sessão e do dispositivo/manager/coleção. Metadados ausentes ou processo encerrado usam nome genérico, sem afetar identidade.
- dotnet restore WaveStudio.slnx --locked-mode aprovado.
- dotnet build WaveStudio.slnx -c Debug --no-restore aprovado com zero erros e zero avisos.
- dotnet test --solution WaveStudio.slnx -c Debug --no-build aprovado com 77 testes, usando TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Dois testes novos tiveram o diagnóstico xUnit2014 durante desenvolvimento e foram corrigidos para Assert.ThrowsAsync antes da verificação final.
- A integração real fez somente leituras e não iniciou reprodução. Escritas foram exercitadas com simulações. O teste específico de leitura encontrou quatro sessões reais com zero avisos e confirmou IDs únicos por endpoint/instância.
- A janela abriu e foi encerrada normalmente. A acessibilidade reconheceu as abas e controles existentes. A captura retornou window capture timed out, e a navegação automatizada não confirmou visualmente o conteúdo do mixer.
- AGENTS.md, docs/REQUIREMENTS.md, NEXT_TASK.md e REVIEW.md são preservados. Os registros abaixo são históricos e suas autorizações/pendências devem ser interpretadas conforme a entrega a que pertencem.
- git diff --check aprovado. Busca por padrões comuns de credenciais sem correspondências no conteúdo versionável. bin/obj conferidos como ignorados. Dependências e lock files preservados. Publicação autorizada pelo proprietário com commit descritivo e push normal, seguida de conferência da árvore e sincronização com origin/main.

### Testes físicos pendentes registrados na F1-I3

Anotar volume e mute de cada aplicativo e do endpoint antes do teste e restaurar ao final. Com dois aplicativos reproduzindo áudio por ação do proprietário, conferir identificação, alteração de uma sessão sem afetar a outra ou outro endpoint, mute/unmute, volume numérico com Enter/Esc, mudanças pelo mixer do Windows, criação/encerramento de processos e troca rápida de saída. Testar acesso negado, remoção/reconexão, arraste contínuo dos controles de endpoint e encerramento durante uma operação. Conferir layout e rolagem em 1366×768, 100/150/200% e Windows 11 x64. A aceitação física da Alpha continua pendente. Ver docs/TEST_PLAN.md.

## Histórico das entregas anteriores

As seções seguintes preservam os registros das Fases 0 e Incrementos 1–2. O estado atual da tarefa é o da seção F1-I6 acima.

### Etapa atual

Fase 0 publicada no commit a3a3358 e aprovada por revisão técnica informada pelo proprietário. Incremento 1 publicado no commit 60238ea após aprovação provisória e autorização de commit/push. Incremento 2 aprovado provisoriamente para publicação e revisão técnica, com commit/push autorizados. A v0.1.0 Alpha segue em desenvolvimento. A integração global tem caminho preferido por endpoints virtuais, com escolha concreta e prova física pendentes nas Fases 2–3.

| Categoria | Estado real |
| --- | --- |
| Implementado | Enumeração, padrões, seleção independente, informações, volume/mute e balanço condicional dos endpoints |
| Código de produto | Desktop e Audio, snapshots independentes de COM, operações serializadas e sincronização externa por polling |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 56 aprovados, zero falhas e zero ignorados. 53 casos de regras/simulações e 3 integrações de leitura Windows, incluindo os 16 anteriores |
| Execução real | Enumeração e leitura de volume/mute dos padrões confirmadas. UI abriu com controles lidos por acessibilidade |
| Testado fisicamente | Escritas de volume/mute/balanço, mudanças externas, comparação com Windows, hotplug e escala de tela pendentes |
| Próximo incremento | Aguardar revisão técnica e configuração do fluxo ChatGPT/Codex pelo GitHub antes do Incremento 3 |
| Dependências futuras | Integração virtual/licença/assinatura e prova externa pendentes |
| Plataforma prioritária | Windows 11 x64 ainda pendente de validação |

### Inspeção do ambiente

| Item | Resultado observado |
| --- | --- |
| Sistema | Windows 10 Pro 22H2 x64, build 19045.6466 |
| SDK | .NET 10.0.404, MSBuild 18.9.15 |
| Runtimes | Microsoft.NETCore.App 10.0.15 e 9.0.22, WindowsDesktop.App 10.0.15 e 9.0.22, AspNetCore.App 10.0.15 |
| Packs | Microsoft.WindowsDesktop.App.Ref, NETCore.App.Ref, NETCore.App.Host.win-x64 e AspNetCore.App.Ref presentes |
| Templates | `winforms`, `winformslib` e `winformscontrollib` disponíveis |
| Workloads | Nenhum instalado, templates e pack desktop já presentes |
| Git | 2.56.0.windows.4, branch main acompanhando origin/main |
| Estrutura inicial | README.md, .gitignore, AGENTS.md e docs/REQUIREMENTS.md, sem solução ou projetos |
| global.json | Ausente, fixação planejada ao criar a solução |

O suporte/ESU do Windows 10 local permanece desconhecido. A execução nesta máquina está classificada como experimental. A aprovação do produto no Windows 11 depende de ensaio nesse sistema. O inventário acima descreve o início da Fase 0. Agora existem WaveStudio.slnx, global.json e os três projetos previstos.

### Verificação do Incremento 1

O proprietário aprovou provisoriamente o relatório, autorizou a publicação com a mensagem `feat: initialize Wave Studio desktop and audio endpoint discovery` e autorizou o início do Incremento 2 após o push. A revisão prévia à publicação conferiu os arquivos implementados, os lock files, a ausência de padrões comuns de credenciais no conteúdo versionável e a exclusão de bin/obj pelo .gitignore. Permanecem pendentes os testes físicos e visuais registrados abaixo.

O commit 60238ea foi enviado a `https://github.com/Wave-Universal-Studio/main.git` e a sincronização com origin/main foi confirmada antes do início do Incremento 2. Foi usada somente no comando a identidade zzThiago e o endereço privado do GitHub já presentes no histórico.

- `dotnet restore WaveStudio.slnx` falhou inicialmente por acesso ao NuGet.Config fora do sandbox. A repetição autorizada restaurou os três projetos com sucesso. O restore em `--locked-mode` passou posteriormente.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou com zero erros e zero avisos.
- `dotnet test --solution WaveStudio.slnx -c Debug --no-build --output Detailed` passou com 16 testes aprovados, zero falhas e zero ignorados, usando `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` na sessão.
- Dependências diretas fixadas em NAudio.Wasapi `[3.1.0]` e xunit.v3 `[4.0.3]`. MTP 2.5.2 e transitivos registrados nos três packages.lock.json.
- A consulta real identificou 67 endpoints. O headset padrão multimídia foi exposto como Alto-falantes (3- USB Audio Device), com mix de 48 kHz, dois canais e contêiner de 32 bits. O microfone padrão expôs 48 kHz, um canal e contêiner de 32 bits. O padrão de reprodução para comunicações era outro endpoint já existente no host.
- O executável `src/WaveStudio.Desktop/bin/Debug/net10.0-windows/WaveStudio.exe` abriu. A ferramenta computer-use confirmou os textos e valores dos controles pela acessibilidade. Capturas falharam por timeout e clique por geometria indisponível. Inspeção visual e troca interativa ficaram pendentes. As regras de seleção independente e preservação após atualização foram verificadas automaticamente.
- As APIs NAudio da tag 3.1.0 foram consultadas e seu uso foi validado por build e execução real. Consultas utilizam COM somente em workers MTA, com descarte dentro da operação. Nenhuma chamada atual escreve configurações de áudio.
- AGENTS.md e docs/REQUIREMENTS.md foram relidos integralmente e preservados. Alterações desta etapa permanecem locais para revisão do proprietário.
- `git diff --check` passou. O Git informou somente avisos de normalização futura LF → CRLF. A janela de verificação permaneceu aberta após a ferramenta detectar interação do usuário, preservando o uso em andamento.

### Comandos e resultados desta etapa

- `Get-Content -Raw -Encoding UTF8` leu integralmente AGENTS.md e docs/REQUIREMENTS.md e inspecionou README e .gitignore. Arquitetura e status estavam ausentes inicialmente.
- `git status --short` e `git status --branch --short` identificaram AGENTS.md e docs/REQUIREMENTS.md já adicionados ao índice pelo usuário. Esses arquivos foram preservados.
- `git --version`, `dotnet --info`, `rg --files`, `git ls-files`, listagens de diretórios e consulta de registro confirmaram o inventário acima.
- `dotnet new list winforms` encontrou inicialmente acesso negado a `C:/Users/thiag/.dotnet`. A repetição autorizada concluiu com código 0 e listou os templates. O SDK inicializou sua configuração e informou instalação automática de certificado de desenvolvimento ASP.NET Core HTTPS. A saída também informou telemetria padrão da CLI. Esses efeitos pertencem à inicialização do SDK.
- Restore, build e test ficam pendentes de projetos, conforme [plano de implementação](docs/IMPLEMENTATION_PLAN.md). Nenhum teste manual de áudio foi executado.
- `git diff --check` passou. Verificação em PowerShell dos links locais em oito documentos encontrou zero destinos ausentes. A tentativa anterior em Python encontrou o executável inacessível, então a verificação foi concluída com PowerShell.

### Próxima etapa

Realizar os passos físicos dos Incrementos 1 e 2 descritos no plano de testes. Aguardar a revisão técnica do Incremento 2 e a configuração do novo fluxo de comunicação entre ChatGPT e Codex pelo GitHub. Ver [arquitetura](docs/ARCHITECTURE.md), [viabilidade](docs/FEASIBILITY.md) e [testes](docs/TEST_PLAN.md).

### Revisão para publicação do Incremento 2

O proprietário autorizou o commit e o push com a mensagem `feat: implement endpoint volume mute and stereo balance controls`.

- Revisados os controles, a leitura inicial, mute independente, limites de volume, validação de canais L/R, nível-base e restauração de balanço após falha parcial. A abertura e a seleção leem os valores existentes. Atualizações externas são consultadas por polling e aplicadas à UI sob proteção contra gerar comandos.
- Confirmados ID/revisão por seleção, cancelamento de pedidos antigos e descarte de respostas anteriores. Chamadas nativas já iniciadas podem terminar no endpoint original, conforme a limitação documentada na arquitetura.
- AGENTS.md e docs/REQUIREMENTS.md conferidos contra o commit a3a3358, com conteúdo original preservado. A arquitetura modular e os lock files foram preservados.
- Inventário versionável e busca de padrões comuns de credenciais revisados, sem credenciais ou arquivos sensíveis encontrados. Saídas bin/obj conferidas como ignoradas pelo Git, com inclusão de fontes, testes, documentação e arquivos de lock na entrega.
- Restore em modo locked aprovado. Build Debug x64 confirmado com zero erros e zero avisos. Os 56 testes passaram novamente, com zero falhas e zero ignorados. As integrações reais são de leitura, e as escritas foram exercitadas com simulações.
- Pendentes os testes físicos de escrita de volume/mute em entrada e saída, balanço audível e centralização, sincronização externa na UI, troca rápida e desconexão durante operação, recuperação/restauração e avaliação visual em diferentes escalas. Registrar e restaurar os níveis anteriores em cada teste físico. Windows 11 x64 permanece pendente e Windows 10 permanece experimental.
- `git diff --check` aprovado antes de preparar a publicação. Após o envio serão conferidos árvore de trabalho limpa e igualdade entre HEAD e origin/main.

### Verificação do Incremento 2

- Mesmas dependências e arquivos de lock do Incremento 1. `dotnet restore WaveStudio.slnx --locked-mode` passou.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou na verificação final com zero erros e zero avisos. `dotnet test --solution WaveStudio.slnx -c Debug --no-build` aprovou 56 testes, zero falhas e zero ignorados, com telemetria MTP desativada na sessão. A suíte inclui a regressão dos 16 testes anteriores.
- O primeiro build encontrou a janela do Incremento 1 ainda aberta e falhou ao copiar a DLL de áudio, com MSB3027/MSB3021 após dez avisos de nova tentativa. A janela foi fechada normalmente e a compilação seguinte passou. Um teste novo também usava `Assert.Throws` para método que retorna Task, corrigido para `Assert.ThrowsAsync` após os diagnósticos CS0619/xUnit2014.
- A integração de leitura observou reprodução em 100%, mute desligado e canais L/R em 1/1, com centro em 0. Captura em 100%, mute desligado e balanço indisponível por mono. Esses eram os valores existentes no host. Os testes reais fizeram consultas. Escritas foram exercitadas somente em endpoints simulados.
- A janela do Incremento 2 abriu. A acessibilidade confirmou sliders, números e botões, com balanço da captura desabilitado e explicação. A captura visual falhou novamente com `window capture timed out`, mantendo a avaliação visual e das interações como pendente. A janela de teste foi fechada normalmente.
- Implementados CoreAudioVolumeAccess, EndpointVolumeState/comandos, EndpointVolumeOperations, EndpointVolumeSession e EndpointControls. Testes novos em EndpointVolumeTests, EndpointVolumeSessionTests e FakeEndpointVolume, além da integração de leitura adicionada à suíte anterior.
- Controles sincronizam por polling de aproximadamente 600 ms. ID e revisão fixam o alvo de cada comando. Trocas de seleção cancelam pedidos anteriores, e a atualização de snapshots na UI fica protegida contra gerar escritas. Balanço exige estéreo FL/FR confirmado e tenta restaurar os níveis após falha parcial.
- A implementação recebeu aprovação provisória para publicação e revisão técnica. Windows 10 continua experimental. Windows 11 x64 e testes físicos de escrita continuam pendentes.
- `git diff --check` passou na conferência final. Os oito arquivos novos foram verificados sem espaços finais ou marcadores de conflito, e os links locais da documentação não têm destinos ausentes. A busca por padrões comuns de credenciais no conteúdo versionável não encontrou correspondências. O índice ficou vazio e o HEAD permaneceu em 60238ea, publicado antes do início deste incremento.

### Arquivos do Incremento 1

Foram criados 19 arquivos versionáveis. Saídas bin/obj são geradas e ignoradas pelo Git.

| Local | Arquivos criados |
| --- | --- |
| Raiz | WaveStudio.slnx, global.json, Directory.Build.props |
| src/WaveStudio.Audio | WaveStudio.Audio.csproj, packages.lock.json, DeviceSnapshot.cs, DeviceSelection.cs, AudioError.cs, CoreAudioDeviceService.cs |
| src/WaveStudio.Desktop | WaveStudio.Desktop.csproj, packages.lock.json, Program.cs, MainForm.cs, DevicePanel.cs |
| tests/WaveStudio.Tests | WaveStudio.Tests.csproj, packages.lock.json, DeviceSelectionTests.cs, AudioErrorTests.cs, CoreAudioIntegrationTests.cs |

Documentos atualizados — README.md, CHANGELOG.md, PROJECT_STATUS.md, docs/ARCHITECTURE.md, docs/FEASIBILITY.md, docs/IMPLEMENTATION_PLAN.md e docs/TEST_PLAN.md. Os links locais foram verificados sem destinos ausentes. Os 19 novos arquivos também foram inspecionados por PowerShell, sem espaços finais ou marcadores de conflito.

### Conferência para publicação da Fase 0

- Todos os documentos criados e modificados foram relidos. AGENTS.md e docs/REQUIREMENTS.md permanecem completos, com as seções e orientações originais, sem diferenças entre os arquivos de trabalho e o conteúdo previamente adicionado ao índice pelo usuário.
- O inventário contém somente documentos Markdown e .gitignore fora dos metadados internos do Git. A inspeção de arquivos, incluindo ocultos e ignorados, e a busca por padrões comuns de credenciais não encontraram credenciais, arquivos sensíveis ou temporários no conteúdo a publicar.
- O remoto origin de leitura e envio corresponde a `https://github.com/Wave-Universal-Studio/main.git`.
- O proprietário autorizou commit e push da Fase 0 com a mensagem `docs: initialize Wave Studio architecture and development roadmap`.
- A publicação foi concluída no commit a3a3358, com main sincronizada com origin/main naquela entrega. Posteriormente, o proprietário informou a aprovação técnica e autorizou o Incremento 1 da Fase 1.
