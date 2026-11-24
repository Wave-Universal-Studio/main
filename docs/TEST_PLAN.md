# Plano de testes

## F5-I2B-NATIVE-PCM-PACKET-ADAPTER (em revisão)

Oito testes novos com buffers sintéticos exercitam captura PCM16 LE → float32, saída float32 e PCM16, silêncio sem payload, pacotes com flags incorretas, endpoint/geração errados, bytes incompletos, NaN/Inf e clipping, invalidação de tickets na saída, formatos incompatíveis e bloqueio de microfone não autorizado no destino externo. Sem COM e sem dispositivos físicos. Validação de PR pendente via GitHub Actions Windows x64; baseline **938 testes comuns + 8 novos**, sete Explicit excluídos. Não anunciar aceitação de streaming OBS/Discord.

## F5-I2A-BOUNDED-PCM-BRIDGE — integração por filas (integrada)

Seis testes novos exercitam dados PCM float32 em memória entre produtores `TryOfferCapture`, `BusMixSession` e consumidores `TryReadOutput`/`TryReadExternal`: somas de fontes, saídas independentes, privacidade do microfone, ausência de dados, backpressure, identificação exata de endpoint, recusa de bloco antigo, ticket de geração inválido e fechamento. Não há abertura de dispositivo nem teste físico. Estes casos complementam os 932 testes comuns da F5-I1. A [CI Windows x64 final](https://github.com/Wave-Universal-Studio/main/actions/runs/45336327862) confirmou **938 aprovados em Debug, 938 em Release**, zero falhas, sete Explicit excluídos por configuração e builds sem avisos/erros. Os testes de rampa de recuperação e reconciliação do mixer foram estabilizados antes do merge.

A etapa não mede latência física, derivações de clocks entre dispositivos, recuperação no hardware, compatibilidade real OBS/Discord ou o isolamento por aplicativo. São requisitos de integração nativa posterior, não resultados simulados.

## F5-I1-BUS-MATRIX-CORE

Os 18 casos novos exercitam o caminho completo de entrega PCM com transporte simulado. Cobrem soma, dois barramentos, ganho e mute independentes, master, pico/RMS e clipping, privacidade e mix-minus, origem agregada, formatos 44,1/48 kHz, mono e estéreo, sanitização, matriz determinística, entrega atrasada e parcial, troca de geração, cancelamento, Stop e zero alocações gerenciadas após aquecimento no cenário medido.

No Linux, o restore locked rodou com `EnableWindowsTargeting=true`, auditoria NuGet de todas as dependências e NU1900 tratado como erro. Os builds x64 Debug e Release da solução usaram `--no-restore --no-incremental -warnaserror` e cross-targeting. O projeto efêmero de validação referenciou a biblioteca de áudio e executou a classe `BusMixSessionTests` sem Windows Forms, Core Audio ou hardware.

O workflow [Testes Windows x64](../.github/workflows/windows-x64.yml) usa `windows-2025` com o SDK de `global.json`, restore locked auditado, builds não incrementais e a regressão comum. Debug usa seed 20261114 e Release usa seed 20261113. O xUnit usa `--culture pt-BR` no processo de testes. A primeira execução em en-US teve 871 aprovados, 61 falhas e sete excluídos em cada configuração. Os logs mostraram expectativas de texto em pt-BR nos testes existentes. Uma execução focada de `AudioErrorTests` reproduziu três falhas em en-US e aprovou quatro casos em pt-BR, sem alterar o sistema. A segunda execução Windows aprovou 925 testes por configuração e encontrou sete timeouts em um teste de posicionamento. Ele aguardava até os controles de mute de endpoints indisponíveis ficarem habilitados. O teste passou a aguardar a seleção e os painéis, preservando as asserções de posicionamento e persistência. A expectativa é 932 testes comuns por configuração (914 da base e 18 novos) e sete Explicit excluídos. Consultar a execução do PR para contagens reais e logs. A regressão de Windows não foi executada no Linux. Os testes novos não leem, abrem ou alteram dispositivos.

## F4-I2-GAMECHAT-SOLO-LANG — balanço e idiomas

Os casos com sessões simuladas verificam centro, atenuação proporcional nos dois lados e nos extremos, instâncias com o mesmo PID, ordem das reduções antes das devoluções, valores externos, ausência de escrita em sessões não atribuídas, duplicidade, geração obsoleta, expiração, falha COM após a escrita, leitura divergente e falha de descarte. Os casos STA verificam reassociação de grupo, proteção dos sons do sistema, restauração antes da mudança de saída, aviso após desconexão e fechamento cooperativo.

A validação de idiomas compara as 450 chaves do catálogo neutro com cada satélite, sem fallback durante a inspeção. São verificadas as ocorrências de parâmetros, alinhamento, formato e espaços de concatenação. Os testes cobrem variantes regionais em Seguir Windows, fallback, preferência persistente, separação entre cultura da interface e cultura numérica, JSON no esquema 3 e controles de idioma em janelas compactas nos três idiomas. As regressões continuam cobrindo a migração dos esquemas 1 e 2, mixer e master, medição, DSP, EQ, teste de microfone e roteamento externo.

O restore locked com auditoria NuGet passou. Os builds x64 não incrementais passaram com zero avisos e erros. Na rodada dirigida, passaram 52 casos. A regressão final acrescentou 41 casos aos 873 da base, com Debug e Release sobre a mesma versão do código e dos testes.

| Regressão x64 | Seed | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug | 20261114 | 914 | 0 | 7 | 98,813 s |
| Release | 20261113 | 914 | 0 | 7 | 62,414 s |

Comandos e critérios da rodada consolidada x64

- `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`
- `dotnet build WaveStudio.slnx -c <Debug|Release> --no-restore --no-incremental -warnaserror --nologo`
- `dotnet test --solution WaveStudio.slnx -c <Debug|Release> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`

Os sete casos Explicit ficam fora da regressão comum. A rodada automatizada não aciona prova física. Os logs locais ficam em `artifacts/f4-gamechat`, excluídos do Git.


## F4-I1-REAL-MIXER — mesa e idioma

Os casos focados preservam a grade e confirmam fader durante atualização, escrita única ao soltar, separação de sessões com mesmo PID, mute, master do endpoint, agrupamento, troca de geração, identidade duplicada, preferências versionadas e fallback de idioma. Os testes STA usam backends simulados para as escritas. Casos de visibilidade verificam que a mesa amostra a saída e que o microfone mantém suas regras de medição. O desenvolvimento encontrou uma asserção antes do carregamento da página de idioma. O teste passou a esperar a página pronta.

Restore locked com auditoria NuGet em todos os pacotes e NU1900 como erro. Builds não incrementais Debug e Release x64 com zero erros e avisos. Foram aprovados 130 testes direcionados, incluindo os seis novos. A primeira regressão Debug mostrou que quatro testes de EQ procuravam um único controle de abas. O seletor do fixture passou a identificar a navegação principal, preservando as asserções de EQ e o teste de proteção de esquema futuro.

| Regressão x64 | Seed | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug | 20261114 | 873 | 0 | 7 | 85,994 s |
| Release | 20261113 | 873 | 0 | 7 | 56,233 s |

Release registrou duas falhas em execuções iniciais ligadas à ausência da pasta ou do arquivo temporário de preferências. O fixture usa agora diretórios exclusivos por processo na saída ignorada de testes, com descarte idempotente restrito ao próprio diretório. As asserções de preservação byte a byte continuam ativas. Os 54 casos focados de preferências e mixer passaram após esse ajuste, seguidos pela regressão Release. A aprovação Debug foi coletada antes desse ajuste exclusivo do fixture, e o código final foi recompilado em Debug. Evidências e logs em `artifacts/f4-real-mixer`, ignorados pelo Git.

Comandos da rodada automatizada

- `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`
- `dotnet build WaveStudio.slnx -c <Debug|Release> --no-restore --no-incremental -warnaserror`
- `dotnet test --solution WaveStudio.slnx -c <Debug|Release> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`

## F3-I3-ROUTE-READINESS-DESKTOP — compatibilidade em leitura

Testes com inventário injetado cobrem presença e ausência do cabo simples, A+B com pares distintos, seleção física incompatível ou removida, Código 10 selecionado e separado, defaults em pinos adicionais, metadados ausentes ou duplicados, formato incorreto, cancelamento, geração obsoleta e falha COM. Os testes STA verificam escolhas acessíveis, guia somente leitura, agrupamento de mudanças de seleção, hotplug durante consulta, rejeição de resultados obsoletos e encerramento cooperativo.

Restore locked com auditoria NuGet completa e NU1900 tratado como erro. Builds x64 não incrementais Debug e Release com zero avisos ou erros. Antes da implementação, a compilação dirigida confirmou a ausência do novo contrato. Na primeira rodada com a UI, a espera de um teste STA expirou porque comparava o texto genérico de estado com a mensagem específica de compatibilidade do fluxo. A asserção foi alinhada à mensagem do fluxo e os 16 casos dirigidos passaram em 8,389 s.

| Regressão final x64 | Seed | Aprovados | Falhas | Explicit excluídos | Duração |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug | 20261114 | 867 | 0 | 7 | 100,552 s |
| Release | 20261113 | 867 | 0 | 7 | 61,884 s |

A DLL Release foi conferida em uma chamada local somente leitura. O inventário encontrou três pinos ativos do cabo simples e duas instâncias PnP, com códigos 0 e 10. Os quatro endpoints escolhidos, o pareamento e os padrões foram confirmados, e A+B ficou como não detectado. Os cenários A+B foram exercitados com inventário injetado. Logs locais em `artifacts/f3-route-readiness`, ignorados pelo Git.

Após integrar `a109426`, o ajuste no código se restringiu a dois textos sobre a prova instrumentada. Os dois builds x64 passaram sem avisos ou erros e os 16 testes dirigidos passaram em 7,353 s.

Comandos finais

- `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`
- `dotnet build WaveStudio.slnx -c <Debug|Release> --no-restore --no-incremental -warnaserror`
- `dotnet test --project tests/WaveStudio.Tests/WaveStudio.Tests.csproj -c Release --no-build -- --explicit off --filter-class '*ExternalRouteReadiness*'`
- `dotnet test --solution WaveStudio.slnx -c <Debug|Release> --no-build -- --explicit off --seed <seed> --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory <pasta>`

## F3-I2-SINGLE-CABLE — transição e quietude

Os testes reutilizam os donos WASAPI e verificam PCM nos dois destinos, atenuação, formatos, geração e sequência. A sondagem simulada cobre quietude, resíduos, ausência de pacotes, retomada de produtor, timestamp, desconexão, cancelamento e falhas de Release/Stop. Restauração observada, conflitos e recursos retidos impedem iniciar a próxima fase. Uma medição isolada verifica zero bytes recorrentes na inspeção dos buffers, com controle positivo do contador. O runner Explicit é descoberto por filtro exato. [Aceitação agrupada posterior](EXTERNAL_ROUTE_PROOF.md#f3-i2-single-cable--avaliação-com-um-cabo).

Em F3-I2-SINGLE-CABLE-PROOF, os binários Release x64 e a evidência automatizada anterior foram reutilizados. A consulta de topologia rejeitou o pareamento antes de qualquer Start. Navegador, quietude e OBS aguardam a mesma execução agrupada após resolução e autorização. [Resultado da pré-verificação](EXTERNAL_ROUTE_PROOF.md#f3-i2-single-cable-proof--bloqueio-antes-do-áudio).

A correção de pareamento acrescenta 12 casos de identidade, incluindo segundo render, problema PnP, instância divergente, driver incompatível, endpoints ausentes, captura ambígua e função física ligada ao cabo. A consulta local Release confirma o pareamento e os padrões sem abrir fluxos. [Resultados](EXTERNAL_ROUTE_PROOF.md#f3-i2-postboot-pairing--diagnóstico-e-correção).

## F3-I1-EXTERNAL-ROUTE — dois fluxos e restauração

Os fakes executam os donos WASAPI e verificam o destino exato, entrega de PCM16/Float32, ganho inicial e atenuação, frames e retenção do guard. Exercitam falta de sinal, Get e Release, desconexão, invalidação, cancelamento e Stop incerto. A configuração verifica os dois fluxos, bloqueia cabo compartilhado e divergência de identidade, e cobre restauração parcial, repetição idempotente, conflito, aplicativo inacessível e endpoint original removido. A medição de zero bytes recorrentes usa a janela de encoder com comparador ativo em subprocesso, com os controles do contador fora da janela.

O apphost Release encontra um método Explicit dedicado com dois fluxos sequenciais. A evidência física futura combina verificação de sinal no headset/OBS, métricas DSP, fila, CPU/RSS, parada e restauração, com confirmação pessoal e sem armazenamento de voz. [Roteiro único e dependências](EXTERNAL_ROUTE_PROOF.md).

## F2-I6D-ALLOC-MONITOR — contagem exata e prova audível preparada

Os quatro casos de zero bytes (diário WASAPI, leitura de EQ, FIR nominal e FIR adaptativo parciais) executam a medição original em apphost separado com filtro exato. O controle negativo e `new byte[37]` confirmam zero e 64 bytes no .NET x64. Um caso força coletas em segundo plano em outro processo enquanto o diário mede sob a mesma asserção de zero. O processo principal mantém o paralelismo padrão. Os demais detalhes e os resultados ficam no [status da etapa](../PROJECT_STATUS.md).

Os fakes exercitam a fachada audível, o mesmo encoder, os owners e a entrega nominal. Verificam consentimento ligado à rota, de uso único e com parada obrigatória, saída SILENT até o corte coerente pós-warmup, PCM16 e Float32 com ganho limitado, contabilidade de frames e cancelamento com buffer emprestado. Seguem na regressão os testes existentes de rampa, deadline, FIFO, proteção de formato, invalidação, Get/Release/Stop e guard. O único método audível Explicit foi listado sem execução. [Roteiro e métricas da tentativa futura](WASAPI_BACKEND_PLAN.md#f2-i6d-alloc-monitor--roteiro-para-revisão).

## F2-I6C-NOMINAL-MONITOR — replay e monitor simulado

O replay usa a série real de 37 leituras de captura da prova diagnóstica. Os pacotes do caso positivo são sintéticos. São testados o motivo de rejeição anterior, a exceção nominal condicionada, o prazo de indisponibilidade, regressão, idade do pacote e HRESULT incerto. O caminho simulado exercita os owners MTA, formato Float32 ou PCM16, taxas de 44,1/48 kHz, drift simulado nos dois sentidos, nível, zero inicial, dropout evitado pelas bordas, cutoff por evidência, troca de seleção, falhas Get/Release/Stop e retenção do guard.

Um impulso atravessa FIR e FIFO e aparece no terceiro bloco de 480 frames no gerador de tempo virtual, com a primeira amostra detectável dentro dos 160 primeiros frames desse bloco. O limite de 30–34 ms pertence à convenção de instantes da fixture, ao preenchimento e ao caminho algorítmico; a prova não mede latência de headset. O ensaio isolado do encoder verifica zero bytes recorrentes após aquecimento. Descoberta do teste `Explicit` dedicado é permitida; execução física exige nova autorização. [Resultados e logs](../PROJECT_STATUS.md).

## F2-I6B2B-DRIFT — integração automatizada e preparo físico

A simulação usa dois owners MTA do `WasapiSilentSession`, o FIR/FIFO de `WasapiSilentPath`, clock virtual, cadência determinística e amostras PCM finitas. Cobre 44,1 ↔ 48 kHz e 48 ↔ 48 kHz, drift zero e nos dois sentidos, fase de ±1 período, granulação, jitter, latência até aplicar comandos, convergência, saturação e ausência de correção com incerteza insuficiente. O ensaio de 45 s virtuais compara razão nominal e adaptativa pela distância de ocupação ao alvo no trecho final.

Os casos de falha cobrem clock rejeitado, wrap do histórico entre cortes, comando expirado, owner com buffer em voo, mudança de seleção, perda na FIFO e falhas em Get, Release e Stop. O fake de render confere SILENT e cada byte do buffer nativo preexistente. Após aquecimento, o ensaio do produtor mede aplicação do comando, FIR, FIFO e publicação de contadores no thread exercitado. A coleta diagnóstica física foi compilada e identificada entre os três Explicit excluídos. [Resultados e comandos](../PROJECT_STATUS.md).

## F2-I6B2A-CHECKPOINT — corte coerente e contabilidade sem hardware

Os casos com relógio determinístico verificam ocupação e área de referência, eventos simultâneos, versões modificadas durante a leitura, perda e wrap de histórico, confirmações ausentes ou tardias, clocks regressivos e futuros, geração e baseline incompatíveis, contagem inconsistente e overflow. A sessão com fake mono de 44,1 kHz e render estéreo de 48 kHz distingue frames nativos e frames de saída, priming, descarte inicial, drops e underruns. O fake render verifica SILENT e preservação dos bytes originais do buffer em cada ReleaseBuffer. Os cenários de Get/Release falho, cancelamento em voo, timeout e Stop verificam ausência de corte parcial e retenção ou liberação do guard.

O orçamento automatizado mede alocação da fixture após aquecimento e zero bytes recorrentes no thread publicador. O caso faz 200 mil publicações sintéticas, com teto de 20 KiB para a fixture e cinco segundos para o lote, sem inferir tempo real físico. As duas provas Explicit permanecem excluídas. [Resultados e comandos](../PROJECT_STATUS.md#estado-atual--f2-i6b2a-checkpoint-18112026).

## F2-I6B1-CLOCK-WINDOW — replay e proteção contra desvios sustentados

A fixture reproduz todos os pontos da prova 3. O teste vermelho confirmou a rejeição estrita da leitura 11. Com a política restrita à captura SILENT, a leitura fica provisória e o replay não confirma transporte. O render mantém o resultado estrito. As sequências marcadas como sintéticas validam recuperação em cinco segundos, tolerância curta inclusiva nos dois sentidos, orçamento inteiro em 44,1/48 kHz e diferentes períodos, falhas de taxa sustentada, envelope acumulado, corroboração insuficiente, frescor terminal, limites de integridade e lacunas/duplicações/sobreposição de pacotes. Fakes de sessão verificam contabilidade e descarte inicial, janela curta insuficiente, janela confirmada, falha de continuidade com release zero, cancelamento e retenção até cleanup.

As regressões comuns excluem os dois testes Explicit. A prova física original não foi reclassificada nem repetida.

## F2-I6B1-CLOCK-DIAG — verificação automatizada

Dois cenários com fakes retornam o mesmo `InvalidPosition` e exigem motivos distintos no JSON terminal, âncora e última leitura aceita preservadas, históricos de captura e render e ausência de IDs. Os dois falharam primeiro pela ausência do histórico e passaram com a instrumentação. Testes determinísticos cobrem a precedência e os motivos de rejeição, excesso e falta de avanço, bordas inclusivas, resto inteiro, posição repetida e prazo de progresso, startup sem âncora, timestamps, qualidade/S_FALSE, RequiresReset, overflow, sobrescrita circular e isolamento dos snapshots. O teste de cancelamento com chamada em voo verifica que o histórico dos owners não é copiado antes do join.

Os cenários simulam ramos do validador. A prova física 2 não contém os deltas necessários para reproduzir sua rejeição; não houve Start físico nesta etapa.

## F2-I6B1-FIX — primeira descontinuidade

Os fakes exercitam descarte integral de flags 0x1/0x3 seguido de transporte válido, primeiro pacote limpo, BUFFER_EMPTY anterior, segunda descontinuidade mesmo dentro da janela, pacote tardio, bits inválidos com descontinuidade, tamanho divergente/zero/excessivo, ponteiro ausente e QPC obsoleto/futuro. As asserções verificam liberação inteira ou zero conforme o consumo, contadores, cancelamento antes ou depois do consumo, manutenção do guard após ReleaseBuffer falho e ausência de produção quando só houve descarte. A proteção de render SILENT continua sendo verificada em cada liberação fake. Clocks em movimento com descarte exclusivo e S_FALSE após descarte são insuficientes para aprovar a prova.

O teste físico continua Explicit e excluído das verificações automatizadas desta correção. A nova tentativa requer revisão e autorização específica.

## F2-I6B1 — transporte silencioso e recuperação

Os testes com fakes verificam owners MTA, ordem de aquisição e liberação, priming antes de Start, padding, buffers vazios/inválidos, flags, clocks incertos/parados/regressivos, cancelamento com chamada em voo, geração, Stop parcial, ReleaseBuffer falho e guard retido. O teste do diagnóstico confirma que completion e falha de Dispose conservam o bloqueio de outras operações.

`WasapiNativeSilentTests` é `Explicit` e fica excluído da regressão. Uma execução futura requer autorização específica para Start e confirmação das pré-condições de `NEXT_TASK.md` e `REVIEW.md`. O teste exige as confirmações `WAVESTUDIO_SILENT_START_AUTHORIZED=1` e `WAVESTUDIO_SILENT_PREREQUISITES_CONFIRMED=1`, resolve os nomes exatos indicados por `WAVESTUDIO_SILENT_CAPTURE_NAME` e `WAVESTUDIO_SILENT_HEADSET_NAME` para um endpoint ativo em cada direção e verifica a prévia fechada. Após cinco segundos de observação registra formatos, capacidade, padding, priming, frames, flags, clocks/HRESULTs e Stop/Release em `artifacts/wasapi-silent`, sem guardar PCM, nomes ou IDs. Resultado físico pendente.

## F2-I6A — clock HRESULT e preparação parada

46 testes automáticos novos de WasapiPreparationTests: S_OK/S_FALSE/−1/falhas HRESULT/sucesso desconhecido, frequência inválida, UINT64 acima de2^53 e deltas/unidades/overflow, identidade/geração e flags, zero parado, rejeição de formato/identidade divergente, sequência de preparação sem Start/buffer PCM, falhas em12 etapas, cancelamento em5 aquisições, liberação inversa no owner, falha de Dispose com pais/lease retidos e chamada bloqueada sem liberação prematura. Guard exercitado contra diagnóstico pendente e sessão pública de produção sem abrir endpoint inexistente. Mantidos os557 anteriores; DSP/fixtures existentes não alterados. Conjunto direcionado final189 aprovado.

Um teste adicional **Explicit**, categoria NativePreparation, só roda com autorização opt-in e dois nomes unívocos selecionados em variáveis de ambiente; exige prévia fechada, fixa IDs/direções e não usa padrão/fallback. É excluído da execução comum para que regressão não autorize inicialização física. Prova Debug autorizada1/1 passou com duas preparações sem Start/GetBuffer de áudio, mesmas sessões para relógio, recursos liberados e sem escrita de níveis/padrões. Formatos/capacidade/períodos/latência/HRESULTs e caminho do relatório no WASAPI_BACKEND_PLAN/status. Não mede drift nativo, latência fim a fim ou áudio em movimento.

Restore locked auditado e builds Debug/Release x64 com 0 erros/avisos. Regressão final: 603 aprovados e 1 Explicit excluído por configuração, 0 falhas, 604 descobertos; Debug70,062s/Release43,054s. Teste nativo separado aprovado1/1; não confundir exclusão padrão com uma prova nativa não executada. Sem kill do watchdog, pipes completos. Não repetir suítes/benchmarks sem mudança ou falha nova.

Falhas durante edição:3 erros de acessibilidade de enums internos em métodos públicos de teste foram corrigidos nos parâmetros;9 avisos de analisadores corrigidos com tokens/assertiva apropriados, sem supressão. Pedido de ajuda `dotnet test -- --help` encontrou FailFast do CLI10.0.404, sem executar casos; consultada a ajuda do próprio apphost de testes, sem atualizar runner. Primeira prova nativa falhou em GetDevice por opção do gerador invertida; cleanup finalmente executado, corrigida para ComObjectWrapper e prova repetida1/1. Sem alterar/contornar HRESULT nativo nem iniciar áudio para “passar”. Regressões finais e eventuais falhas reais registradas no PROJECT_STATUS. A14 aprovado por relato geral; demais subcasos físicos/Windows11 pendentes.

## EQ-PROFILE-1 — ajustes e perfis

38 casos novos:31 de modelos/JSON/armazenamento e7 de UI (quatro integrações Windows de janela/armazenamento, sem áudio). Cobrem cópias independentes, limites de ganhos/preamp/nomes/quantidade, CRUD e IDs, ajuste atual distinto do perfil, migração v1 em memória com campos antigos e defaults de janela, backups v1/v2, campos desconhecidos/duplicados/ausentes, conflitos/versões futuras, falha de escrita/retry, arraste consolidado/flush no fechamento, duas aberturas isoladas, nenhuma captura/playback automática, 20% digital, clipe vazio e congelamento/formato incompatível. Testes de versão futura existentes passaram a usar3, pois2 agora é suportada.

A primeira regressão Debug teve556/557 aprovados; MeterVisibilityTests também falhou isoladamente ao esperar o fechamento na fixture síncrona com DoEvents. Reutilizado o loop STA assíncrono existente, preservando prazo/assertivas e produção de medição; os dois casos direcionados passaram. A execução seguinte terminou após489 aprovados com exceção FlowLayout/índice, em continuação de MicrophoneTestPanel.RunAsync na thread pool. A fixture de microfone foi convertida para o mesmo loop de mensagens existente, mantendo os testes e prazos; callbacks deixam de depender de um contexto de UI não instalado. Nenhuma correção especulativa no layout de produção. Isso não comprova a causa histórica completa de intermitências do runner. Resultados das regressões finais Debug/Release no PROJECT_STATUS. A14 manual no ALPHA_ACCEPTANCE, demais casos físicos e Windows11 pendentes.

## CHECKUP-1 — liberação, encerramento e snapshots

24 casos novos:19 CheckupMicrophoneTests,2 CheckupShutdownUiTests (integrações Windows somente leitura/janela) e3 CheckupTransportTests. Nove falharam contra a base antes do patch, reproduzindo cleanup ignorado com erro/descarte e provider/modo pré-Start retidos. Cobertura de captura/reprodução × clear/select/invalidation/close, erro primário+Dispose, falhas RequestStop/Tick, worker/stop bloqueado e timeout/retry sem descarte ativo; completion com erro durante close de ambos os papéis. Mantidos testes existentes e suas asserções.

STA reutiliza fixture de preferências, espera de inicialização observada e áreas úteis injetadas (sem alterar telas). Prova janela aberta/diagnóstico não modal no timeout, cleanup independente após exceção no aviso de armazenamento e segunda tentativa segura. Primeiras tentativas desse caso fecharam antes de executar Shown: corrigido o preparo do teste para esperar startup real, sem mudar critério de produção. Não atribuir essas ocorrências à causa histórica de runner.

Fake de transporte injeta snapshot ausente/exceção: falha latched, gate invalidado, média null, flag explícita e últimos contadores conhecidos; não substitui falta por zero. Callback de start é recusado antes de habilitar gate, sem consumir priming. Watermark/priming continuam limitações documentadas antes de F2-I6B, sem prova nativa.

Restore locked auditado, builds x64 e regressão final completa495+24 por configuração; resultados/tempos finais e comandos no PROJECT_STATUS. Testes direcionados durante edição incluíram167 aprovados antes da última ampliação de um caso de close. Não iniciam gravação/reprodução física. Roteiro físico A01–A13 e fechamento normal/USB/suspensão permanecem pendentes; não instalar driver, mudar Windows ou medir latência física nesta tarefa.

Suítes finais: **519 aprovados em Debug e519 em Release**,0 falhas/ignorados;61,510 s/30,756 s, sem kill do watchdog e com pipes completos. As suítes anteriores518 passaram antes de um caso adicional reproduzir a mensagem incorreta de Clear enquanto recursos continuavam retidos. Corrigida a mensagem para descarte pendente, seguida das duas suítes finais. Startup/close normal de instância própria do pacote: código0, settings isolado, sem áudio. Não repetir suites/benchmarks após aprovação sem nova causa.

## EQ-PREVIEW-1 — prova audível local

54 testes novos: 51 EqualizerPreviewTests (40 casos centros/ganhos + 11 de regras/provider/ciclo) e três EqualizerPreviewUiTests, reutilizando fakes/fixture de microfone. Resposta de cada banda em ±15 dB, 44,1/48 kHz, tolerância ±0,12 dB contando preamp/headroom; canais, estabilidade, PCM16/float32, flat/bypass, clipping/sanitização, memória original, EOF/blocos, cancelamento/invalidação, estado novo por play e zero alocação recorrente na leitura. UI: dez valores reais/reset, default desligado/20%, parâmetros congelados e EQ indisponível pela taxa efetivamente gravada. Nenhuma reprodução/captura física nos testes novos.

Conjunto direcionado final: 100 aprovados. Houve duas execuções com 99 aprovados/um timeout no teste antigo de limpar; diagnóstico adicional mostrou Clear desabilitado apesar de Play habilitado. Reordenação experimental apenas transferiu a corrida para a asserção Play após observar Record; descartada. Espera do teste passou a exigir os controles pertinentes juntos, preservando suas asserções; produção/runner não foram refatorados por hipótese. Primeiro Debug completo: 494/495 aprovados, uma falha porque PreferencesUiTests identificava Seguir padrão de captura pelo último checkbox, agora pertencente ao EQ; consulta alterada para texto explícito, mantendo asserção. Depois, 21 testes direcionados de UI/preferências aprovados. Suítes completas finais Debug/Release, resultados e comandos no status. Não atribuir resolução da intermitência histórica a essas correções de fixture.

Teste físico pendente: [A/B e limites](EQ_PREVIEW.md), caso A13 do roteiro Alpha. Conferir em fones/volume baixo original, cortes/boosts com headroom, bypass/reset, parar/limpar, formatos, USB/seleção e encerramento. Demais A01–A12 e Windows 11 pendentes. Sem release pública ou novo motor contínuo.

Suítes finais: **495 aprovados em cada configuração**,0 falhas/ignorados; Debug59,994 s/Release29,826 s. Restore auditado/builds x64 sem erros/avisos.441 regressões anteriores preservadas,54 novos,21 integrações Windows existentes. Startup/fechamento próprio do executável Release: código0, armazenamento isolado, nenhum áudio. Publicação local/backup e hashes no PROJECT_STATUS; sem nova inspeção visual.

## Histórico — F2-I5, contrato WASAPI isolado

Regressão integral dos 375 anteriores mais 66 novos: 32 WasapiContractTests, 32 IsolatedTransportTests e duas consultas WindowsIntegration em WasapiProbeTests. Restore locked com auditoria de todos os pacotes; builds Debug/Release x64; suítes completas com watchdog de180 s, MTP165 s e sementes20261114/20261113. Contagens/tempos finais e hashes da republicação no PROJECT_STATUS. Testes físicos e aceite Alpha não são inferidos destes resultados.

- Formatos: construtores/propriedades públicos NAudio 3.1.0, PCM16/float32, mono/LR, extensível/GUID/bits/align/bytes/extra/máscara, incompatibilidades, endpoint/geração/estado e orçamento de períodos. Mono não inventa posição; container32 não implica float.
- Clocks: deltas/remainders inteiros acima de 2⁵³ também no QPC, overflow, timestamp/regressão/salto/estagnação, qualidade incerta/ausente, silent/error/discontinuity, outra identidade/geração, reset retido. Nenhuma leitura de clock físico ativo.
- Sessão: preparação/início parcial, ordem/afinidade MTA, produtor/consumidor separados, priming efetivo, FIFO por hash contra referência FIR/ganho, PCM16↔float32, mono→LR, blocos irregulares/limites, silêncio com payload vazio, default mute e atenuação digital. FIR de degrau tem ringing: ganho em regime0,1 verificado separadamente de pico ≤0,12.
- Média temporal: áreas acumuladas por eventos, mesma contagem final/tempo diferente, exemplo1184 frames, watermark obrigatório, intervalo/contador futuro recusados. Controlador chega a Tracking; clock fake+300 ppm por 5 s move alvo para−100 ppm e razão efetivamente aplicada para−97,5 ppm, sem perdas. É início do slew, não prova de convergência prolongada; testes longos da F2-I4 permanecem.
- Falhas: drops/underruns reais→ReprimeRequired, timestamps/clock indisponível/descontinuidade, desconexão/suspensão simuladas, backend que falha, stop/cancel durante prepare, stop durante submit, antiga geração/ID, reentrada/afinidade, callbacks concorrentes versus Stop. Barreira de espera existe somente no fake de teste; callback de produção não espera.
- Encerramento: timeout de1 s preserva recursos em uso/interlock, recuperação manual e DisposeAsync repetido após quiescência. Fila de oito comandos e256 pedidos concorrentes contra descarte completam ou rejeitam, sem tarefa aceita após encerramento. Lock de selagem existe apenas no controle. Interlock diagnóstico/transporte testado; Alpha ainda não conectada.
- Custo: após aquecimento,100 pares de callbacks com480 frames e FIR/SPSC/codificação/hash; zero bytes alocados na thread medida. Tempos de parede sem asserção de benchmark ou alegação de deadline físico.
- Consulta nativa: primeiro endpoint ativo de cada direção, ID/direção explícitos, mix/capacidade/períodos ou erro detached, recursos descartados em MTA. ID inexistente sem fallback e cancelamento. Buffer/latência null e StreamInitialized=false obrigatórios. Sem Initialize/Start, gravação, reprodução ou escrita de volume/padrão.

Relatórios numéricos locais em artifacts/wasapi-backend e logs de prazo/STA em artifacts/clock-drift, ignorados pelo Git. Execuções direcionadas intermediárias tiveram quatro falhas por comparar pico do degrau FIR com o ganho em regime; teste passou a verificar ambos separadamente, sem mudar FIR/ganho ou esconder clipping. Ocorreram erros de compilação intermediários ao conferir assinatura Stop(long), tipo público da máscara e overload assíncrono de assert; corrigidos, sem alteração de dependências. Nenhum novo timeout do runner observado nas execuções completas desta etapa; causa histórica permanece sem comprovação.

Republicação local: conferir processo do proprietário fechado, staging Release x64 framework-dependent, recursos/PE/metadados/licenças/SHA256, abrir/fechar instância própria com --settings-file isolado e promover somente arquivos conhecidos com backup. Não tocar preferências ou encerrar processo do proprietário. Roteiro físico permanece ALPHA_ACCEPTANCE A01–A12, incluindo hotfix de área útil, DPI, USB/suspensão, controles/áudio ativo, mic e medidor. Windows 11 pendente; Windows 10 experimental. Nenhuma ativação de backend nativo ou monitoramento em F2-I5.

## Histórico — F2-I4, resampling adaptativo de amostras

Restore locked auditado; builds Debug/Release x64 com zero erros/avisos. Suítes finais: 375 aprovados por configuração, zero falhas/ignorados (336 anteriores + 39 novos). São 356 regras/simulações/arquivos/STA/sintéticos e 19 integrações Windows de consulta/janelas; nenhum novo stream de captura/reprodução ou alteração de volume/padrão.

- ClockEstimatorWindowTests: dois casos de rejeição cumulativa com/sem Latest prévio, reset obrigatório e retomada. Deltas locais plausíveis não podem validar janela longa impossível.
- AdaptiveResamplerTests: nove casos para blocos irregulares/saída unitária, equivalência de sequência, mapeamento/sanitização somente de frames aceitos, contadores de proteção, reset/EOF com fase/razão efetiva, limites/overlap/zero-space, geração obsoleta/parada e precisão/carry Q48 acima de 2⁵³/overflow sem mutação.
- AdaptiveResamplingQualityTests: 16 casos, incluindo nove configurações de taxa/ppm extremo/centro com DC/senos/alias/imagem, três impulsos L/R/atraso, três transições por referência de fase variável/seam e um custo/alocação/consumo parcial. Metas definidas antes de medir, atendidas na grade; relatórios e limites em [ADAPTIVE_RESAMPLING_QUALITY.md](ADAPTIVE_RESAMPLING_QUALITY.md).
- AdaptiveIntegrationTests: cinco casos: quatro pares ligado/desligado com sinais gerados e clocks independentes, ChannelAdapter/FIR/SPSC realmente processando amostras e hash FIFO, mais parada ReprimeRequired/descarte/reset/geração antiga/retomada. 100 s simulados em 48→48 ±300 e 35 s em 44,1↔48 ±100, oito caminhos/540 s simulados. Modelo anterior de 900 s continua na regressão. Nenhum arquivo de áudio.
- PreferencesUiTests: sete casos adicionais de áreas úteis vazias/inválidas/mistas, coordenadas negativas, dimensões pequenas, monitor removido e preferência fora da tela/maximizada, sem exceção de startup ou perda da posição válida por fallback. Seleções habilitadas após consulta assíncrona e seleções/níveis persistidos preservados. Os 13 casos usam Application.Run/esperas assíncronas, deadline externo à STA, Join, coletor de avisos e todas as asserções; nenhum teste desabilitado.

Ocorrências reais: um Debug intermediário teve 342 casos aprovados e erro de execução por exceção não tratada de área útil inválida; guardas de MainForm corrigidas e verificadas inicialmente por três casos, ampliados para sete após bd0ab52. Um Release completou 371 com 370 aprovados e um timeout de 20 s após Disposed na segunda abertura; fixture reentrante DoEvents revisado. Classe isolada depois da revisão: nove aprovados em Release. Na ampliação, houve 6 aprovados/7 falhos porque a asserção de DevicePanel.Enabled ocorria antes da consulta assíncrona terminar; corrigida a espera, preservando a asserção, 13 aprovados. Suítes finais com o complemento: Debug seed20261114 50,628 s; Release seed20261113 23,694 s, 375 cada. Repetições anteriores não são omitidas nem consideradas prova de causa histórica resolvida. Não houve kill do runner das suítes finais.

Watchdog revisado foi exercitado com probe inofensivo compilado localmente (fonte em scripts/Test-Watchdog.ps1; artefatos ignorados): timeout pai/filho e pai encerrado com filho conservando stdout. Códigos 124/125 esperados; coleta completa, filho confirmado encerrado. Conhost sem nonce foi recusado, não morto; identidade ambígua nunca autoriza kill. Prazo de pipes/fim de processo incluído no orçamento; ferramentas/segurança/ExecutionPolicy preservadas. Logs em artifacts/clock-drift e métricas em artifacts/adaptive-resampling, ignorados pelo Git.

Comandos: restore locked auditado; build Debug/Release --no-restore; `& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 scripts/Test-WithWatchdog.ps1))) -RepositoryRoot (Get-Location).Path -Configuration Debug -Seed 20261114 -TimeoutSeconds 180` e Release/seed20261113. Orçamento externo 180 s (inclui coleta), processo até 170 s e timeout MTP 165 s; aumento deliberado e limitado para a nova carga de FIR/sinais, sem asserção de benchmark dependente da máquina. Suítes finais abaixo desses limites. Probes usam 30 s. Não alterar política de execução para rodar os scripts.

Pendências: revisão F2-I4, causa completa da intermitência, backend real F2-I5, áudio físico, latência acústica, A01–A12 e Windows 11; Windows 10 experimental. Não foi republicado o pacote; correção de área de tela está no código/builds, preservando o executável de prévia. Não iniciar F2-I5, monitoramento, EQ, redesign ou Fase 3.

## Histórico F2-I3 — relógios independentes e estabilidade

Em 14/11/2026: restore locked auditado, builds Debug/Release x64 com zero erros/avisos; suítes finais com 336 aprovados por configuração (313 anteriores + 23 novos), zero falhas/ignorados. São 324 regras/simulações/arquivos/STA/sintéticos e 12 integrações Windows existentes. Nenhum novo teste abre streams ou altera configurações de áudio.

- ClockDriftTests: 22 casos; identidade/geração, timestamp repetido/invertido/futuro/obsoleto, qualidade, posição regressiva/salto extremo, referência comum, offsets próximos de long.MaxValue, descontinuidade/reset, orientação de razão, waiting, slew/limites, anti-windup e falha persistente. Simulação inclui 18 pares ligados/desligados × 900 s, três pares de taxas e ±50/100/300 ppm, jitter, offsets comuns/independentes, determinismo, drift fora do orçamento, pausa/reinício, 41 observações inválidas e recuperação. Contadores priming/perdas/descarte/recuperação separados; invariantes inteiras e fila limitada verificados.
- NominalPartialConsumptionTests: um caso com duas capacidades de saída (512/um frame), mesmo hash/contagem das amostras e zero alocação recorrente. 55.559 contra 27.395.275 frames oferecidos/remapeados para 8.192 consumidos: custo redundante documentado, sem alterar o FIR nominal.
- PreferencesUiTests: seis casos existentes preservados; coletor de avisos em formulários invisíveis com asserções, fases/thread/timestamp do caso intermitente, prazo externo à STA e Join limitado. Caso de falha de gravação continua exigindo aviso. Watchdog de processo próprio disponível; ramificação de encerramento não foi acionada neste ensaio. Nenhum teste desabilitado ou aviso real de produção removido.

Tentativas limitadas: seis casos isolados aprovados, regressão inicial de 313 aprovada e quatro execuções ampliadas de 336 aprovadas (Debug/Release, sementes 20261113/20261114), sem runner incompleto ou encerramento forçado. Duas tentativas intermediárias dos 18 casos de relógio falharam na meta de convergência antes da correção da observação de ocupação; corrigida por média temporal, sem relaxar critérios. Não atribuir essas falhas à intermitência de UI. Causa do bloqueio histórico não comprovada; hipótese modal/DoEvents/teardown registrada, sem stack gerenciado disponível ou alteração de produção por suposição.

Comando reproduzível com watchdog e diagnóstico, após restore/build: `& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 scripts/Test-WithWatchdog.ps1))) -RepositoryRoot (Get-Location).Path -Configuration Release -Seed 20261113`. Limite externo 90 s, interno 80 s; logs em artifacts/clock-drift, ignorados. Não altera ExecutionPolicy, não instala ferramenta e não eleva o Windows. [Protocolo completo, métricas e limitações](CLOCK_DRIFT_SIMULATION.md).

Pendentes: revisão F2-I3, causa da intermitência histórica, resampler adaptativo de amostras e regressão espectral de transições, backend real, A01–A12, latência acústica e Windows 11. Windows 10 experimental. Preservada a prévia existente; nenhum novo controle foi integrado à UI. Não iniciar integração física até revisão/evidência Alpha, nem avançar a F2-I4 nesta etapa.

## Histórico F2-I2 — canais e resampling nominal

Restore locked com NuGetAudit=true/Mode=all aprovado; builds Debug/Release x64 com zero erros/avisos. Suíte completa: 313 aprovados por configuração (283 anteriores + 30 novos), zero falhas/ignorados; 301 regras/simulações/arquivos/STA/sintéticos e 12 integrações Windows existentes. Nenhum teste novo abre áudio físico ou escreve volumes.

- NominalResamplingTests: 23 casos para políticas mono/LR, anti-fase, não finitos e extremos finitos no FIR, limites/overlap, identidade bit a bit, impulso/canais, continuidade com blocos irregulares e saída de um frame, reset/EOF/flush, contagem exata em mais de 88 mil frames por cenário e limites de memória. Fórmula de contagem também verificada para 24 horas e overflow rejeitado, sem afirmar execução de um dia de áudio. Trajetórias mono48→estéreo48, estéreo44,1→estéreo48 e estéreo96→mono48 passam pelo SPSC original; contadores de drop/underrun são de frames de saída. A contagem usa fase racional inteira, sem aproximar a razão em cada bloco.
- PipelineLifecycleConcurrencyTests: cinco casos; três ações com 32 corridas cada (Stop/cancelamento/Dispose), produção/consumo em threads de teste, reset/descarte após Join; dois callbacks float/PCM atrasados depois de reinício devem ser rejeitados pela geração. Prazos por participante e timeout global; nenhuma barreira adicionada ao callback produtivo. Stress limitado não demonstra todos os interleavings.
- ResamplingQualityTests: dois casos, grade de DC/senos/alias/imagem com metas fixadas antes da avaliação e medição de custo/alocação após aquecimento. Ganho máximo observado 0,00148773 dB, pior alias -81,11294 dB, imagem -123,96323 dB; zero bytes alocados na thread medida. Método, ripple, bandas, memória e Debug/Release em [RESAMPLING_QUALITY.md](RESAMPLING_QUALITY.md).

Execuções finais completas: Debug 11,821 s e Release 9,238 s, com dotnet test --solution WaveStudio.slnx -c Debug/Release --no-build -- --timeout 90s --long-running 15, diagnóstico MTP habilitado; Release final com --seed 20261113 e TESTINGPLATFORM_TELEMETRY_OPTOUT=1. Duas outras tentativas ficaram incompletas, com 304 aprovados e sem falha de caso reportada antes do encerramento somente dos runners próprios. Diagnóstico Release identificou em andamento PreferencesUiTests.MainWindowReloadsAbsentPreferenceAcrossTwoStartsWithIsolatedStorage, caso preexistente; os seis da classe passaram isoladamente. Timeout de 90 s não encerrou o processo travado. Causa/interleaving não comprovados, sem alteração de runner/testes antigos/produção; logs locais ignorados em artifacts/engine-foundation/diagnostics-* e f2i2-*.txt. Registrar a intermitência para revisão, sem afirmar correção. Totais de retorno em operações já em voo não são transação com Stop/cancelamento ou autorização de envio físico; detalhes em [AUDIO_ENGINE_PLAN.md](AUDIO_ENGINE_PLAN.md).

Pendente: revisão técnica F2-I2, simulação de drift/jitter/razão adaptativa e backend futuro, latência acústica e aceitação física Alpha A01–A12; Windows 11 sem ambiente validado e Windows 10 experimental. Executável publicado, Desktop, recursos e áudio existentes preservados; não há novos controles a testar na UI nesta etapa.

## Histórico F2-I1 — fundação sintética isolada

Restore locked com auditoria aprovado. Debug/Release x64 sem erros/avisos; 283 testes aprovados por configuração, zero falhas/ignorados (252 anteriores + 31 novos): 271 regras/simulações/arquivos/STA/sintéticos e 12 integrações existentes. Estado, formato/limites, conversão, interleaving, overflow/underrun, silêncio, wrap/rollover, cancelamento/geração/reset/descarte e NaN/Infinity/clipping testados em EngineFoundationTests. Quatro cenários SPSC concorrentes de 100.000 frames, preservando ordem/canais e contadores finais. Nenhum novo teste abre stream físico ou reproduz áudio.

EnginePerformanceTests mede PCM16→float32→fila→proteção neutra após aquecimento: 51.200.000 frames estéreo, 0 bytes alocados na thread medida; Debug 2.590,9652 ms, Release 1.152,6465 ms. CPU do processo no intervalo 3.156,25/2.187,50 ms, incluindo runner/JIT/runtime; não atribuir integralmente ao motor. Fila de 1.024 frames a 48 kHz tem capacidade teórica de 21,333 ms, não latência acústica. Relatórios locais em artifacts/engine-foundation; método, números e limites em [AUDIO_ENGINE_PLAN.md](AUDIO_ENGINE_PLAN.md).

Uma execução completa inicial ficou incompleta e somente o runner próprio foi encerrado; 31 novos aprovados isoladamente e repetição completa limitada aprovada em ambas as configurações. Causa inicial não determinada; sem falha crítica reproduzida da base. Avisos iniciais de token xUnit corrigidos. Comando recomendado: dotnet test --solution WaveStudio.slnx -c Release --no-build -- --timeout 60s --long-running 10, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.

Antes de integração física: revisão F2-I1, concluir regressão Alpha A01–A12, validar negociação/ownership WASAPI, resampling/canais, relógios/drift, recuperação/stop e monitoramento manual em headset/nível baixo. Precisar de autorização específica antes de abrir áudio físico em automação. Windows 11 continua pendente, Windows 10 experimental. Não foi republicado o executável aberto pelo proprietário, nem ativado pipeline experimental na UI.

## Identidade visual — 13/11/2026

Restore locked com auditoria, Debug/Release x64 sem erros/avisos; 252 aprovados em cada configuração (248 anteriores + quatro BrandingTests), zero falhas/ignorados. Verificados SHA-256 e dimensões do original, sete quadros PNG do ICO, carregamento Windows, alfa do PNG incorporado, clones após fechamento de streams e metadados de produto/projeto/versão. Testes iniciais detectaram AssemblyTitle inadequado e fallback GDI no pedido 256; título corrigido e dimensão 256 validada no próprio quadro PNG. Suítes finais aprovadas.

Extração de ícone do EXE e metadados nativos conferidos; app publicado abriu e fechou normalmente com configuração isolada, sem áudio. Pendente inspeção humana de cabeçalho, taskbar, Alt+Tab e futuros atalhos, DPI 100/150/200% e Windows 11. Ícones em cache ou atalhos fixados não foram alterados. Testes físicos anteriores da Alpha permanecem pendentes conforme roteiro.

## F1-I6-METER — medidor de entrada, 13/11/2026

Restore locked com auditoria aprovado. Builds Debug/Release x64 com zero erros e avisos; 248 testes aprovados em cada configuração, zero falhas/ignorados. Regressão dos 228 anteriores + 20 novos: 236 regras/simulações/arquivos/STA e 12 integrações Windows de leitura/execução isolada.

MicrophoneMeterTests verifica percentuais zero/intermediário/máximo, texto e acessibilidade, indisponibilidade distinta de zero, rejeição de valores inválidos, timestamp ausente/expirado, geração/endpoint obsoleto, troca, desconexão, invalidação com mesmo ID, pausa, fechamento e falha do worker/driver. MicrophoneTestUiTests acrescenta recepção do pico com captura desligada, nível digital ainda em 20% e invalidação na recuperação. MeterVisibilityTests cobre leitura em andamento cancelada pela pausa/retomada, reutilização da fonte e uma janela real STA com worker/microfone simulados, alternando abas, minimizando/restaurando e fechando sem nova captura automática. Essa integração usa o catálogo/controles existentes em leitura, com arquivo de configuração isolado.

O teste da janela detectou inicialmente medição desabilitada na primeira exibição: a seleção inicial da aba ainda não estava disponível na atualização anterior ao Show. Corrigida a atualização no Shown, após a inicialização. Suítes finais aprovadas. Os 20 casos novos não abrem streams de microfone real nem reproduzem áudio; simulações não validam fala, silêncio, ruído, qualidade ou particularidades do driver.

Pacote Release AMD64 conferido com 14 arquivos, dependências/avisos e hashes em manifesto local. Executável da pasta habitual iniciou com título esperado e encerrou normalmente com configuração isolada; criou somente settings.json/lock, com zero preferências de níveis. Não foram acionados gravação ou playback, nem encerrado processo do proprietário. A inspeção visual completa e a resposta a fala/ruído são pendentes.

Conferir fisicamente A12 na nova prévia: mesma entrada no Windows e Wave, fala/silêncio/ruído, estado numérico e indisponibilidade, troca de entrada, mute, desconexão, pausa/retomada e leitura durante gravação explícita. A11 teve relato inicial de zumbido sem voz no Wave/Windows e troca USB-C/C por C/A sem resolver o problema informado. O complemento remoto ed19c6e registrou sucesso posterior da gravação/reprodução após ajustes próprios não especificados. Sucesso relatado na função principal; descarte, limite, mute, troca/desconexão e recuperação continuam pendentes. Não atribuir causa. Windows 10 segue experimental; Windows 11 x64 e layout/DPI aguardam validação física.

## F1-I6-MIC — captura/reprodução curta, validação física pendente

Restore locked e auditoria aprovados após repetir com acesso ao NuGet (a tentativa restrita teve três NU1900). Debug/Release x64 compilados sem erros/avisos. Em cada configuração, 228 aprovados, zero falhas/ignorados: 184 de regressão + 44 novos, totalizando 217 regras/simulações/arquivos/STA e as 11 integrações Windows de leitura/execução isolada existentes.

MicrophoneTestTests verifica ausência de captura/reprodução automáticas, IDs exatos sem fallback, exclusão mútua, duração/bytes/frames, parada nativa pendente, mono, subconjunto de formatos, rejeição de formatos/níveis inválidos, atenuação PCM/float/zero/unidade, NaN/Infinity, gerações e callbacks obsoletos, abertura bloqueada cancelada por troca/Parar/Limpar, erros/permissões, conclusão/watchdog, falha durante playback, descarte e fechamento idempotente. MicrophoneTestUiTests exercita botões reais com backend simulado em STA, nível inicial de 20%, seleção sem efeitos e mudança de alvo. As camadas de teste não possuem API para gravar configurações ou escrever volume/mute do Windows; a revisão confirmou ausência dessas chamadas no diagnóstico.

As capturas e reproduções automatizadas usam exclusivamente fakes; nenhum microfone real foi aberto e nenhum áudio foi emitido pelo teste novo. O executável atualizado abriu com título esperado, fechou normalmente e gerou apenas settings.json e seu lock em diretório próprio, com zero preferências de níveis. A tentativa inicial com janela oculta não expôs MainWindowHandle para CloseMainWindow; somente esse processo próprio de ensaio foi encerrado. O ensaio visível seguinte passou. O processo anterior do proprietário foi preservado.

Executar o novo caso [A11](ALPHA_ACCEPTANCE.md) com fones e volume baixo: gravar/falar/parar, conferir ausência de reprodução automática, ouvir explicitamente, parar, repetir e limpar. Conferir mono, mute/permissões, formatos diferentes, troca de dispositivos, USB, suspensão e fechamento durante atividade. A01–A10 continuam pendentes de resultados completos, com volume/mute do microfone já confirmados parcialmente pelo proprietário. Windows 11 aguarda ambiente. Abertura de janela e simulações não confirmam qualidade audível, latência ou negociação física.

## F1-I6 — checkpoint técnico, validação pendente

Restore locked aprovado com auditoria de dependências após repetir com acesso ao NuGet. A primeira tentativa teve NU1900 por restrição de rede. Builds Debug/Release x64 terminaram com zero erros e zero avisos. Em cada configuração, 184 testes passaram, zero falhas e zero ignorados, incluindo regressão dos 174 anteriores. São 173 casos de regras/simulações/arquivos/STA e 11 integrações Windows. Runner com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.

Regressões novas verificam arquivo excessivo protegido e preservado byte a byte em save/reset/recuperação, crescimento concorrente com backup mantido, modelo inválido com 1.001 endpoints/janela fora do limite, aviso e fechamento após falha previsível, seleção bloqueada antes da carga e fechamento cancelando a carga pendente. Preferências de controles com erro e balanço em silêncio são verificadas. Os testes de mudança de seleção/nível durante a prévia permanecem na regressão.

Restauração verifica mudança de canais no unmute final, inclusive base baixa, quantização dentro de 2% da base e balanço não observável em zero. Picos de sessão têm testes de manutenção de linha/foco, silêncio, pausa, atraso, indisponibilidade e retorno de geração anterior. Escritas de áudio continuam em backends simulados.

Integração real consultou quatro sessões, com quatro medidores disponíveis e pico zero, sem avisos de enumeração. Cem consultas do mixer com picos tiveram média de 1,68 ms. Endpoint completo teve média de 1,42 ms em dez leituras. A UI do mixer usa 1 Hz. Resposta a fala/música e picos breves exige o roteiro físico.

O executável Release publicado, PE AMD64, foi iniciado com --settings-file temporário, permaneceu ativo por 600,34 s e encerrou normalmente após CloseMainWindow/WaitForExit. Configuração v1 foi criada no fechamento, com modos automáticos, janela salva e zero preferências de níveis. A coleta de 61 amostras observou CPU de 1.125 ms, equivalente a 0,187% de um núcleo no intervalo incluindo inicialização. Após 60 s, média de 0,0087% de um núcleo, working set de 66.187.264 a 75.751.424 bytes, handles de 362 a 371, terminando em 362. Working set final de 75.460.608 bytes, private bytes de 31.727.616 e 12 threads. Os números descrevem esta execução e duração.

A consulta de Application Error/.NET Runtime no intervalo encontrou zero eventos correspondentes. O campo Responding retornou true nas amostras, com inspeção visual indisponível. Computer Use falhou por pipe nativo indisponível, inclusive após a recuperação limitada e pela superfície unificada. A alternância real de abas, layout, diálogos e recuperação física no processo publicado permanecem pendentes. O ensaio foi iniciado no estado padrão da janela. Os testes STA verificam o comportamento da grid separadamente.

Os avisos de licença acompanham a pasta publicada, que contém o aplicativo e suas dependências de execução. Artefatos e relatório detalhado ficam ignorados. Roteiro assistido em [ALPHA_ACCEPTANCE.md](ALPHA_ACCEPTANCE.md), com casos A01–A10 aguardando relato do proprietário. Windows 10 Pro 22H2 é experimental. A compatibilidade Windows 11 x64 aguarda ambiente/ensaio. O checkpoint técnico mantém a aceitação física da Alpha pendente.

## Histórico F1-I5 — resultados

Restore locked aprovado, build Debug x64 com zero erros/avisos e 174 testes aprovados, zero falhas e zero ignorados. São 165 casos de regras, simulações, arquivos temporários e WinForms STA e nove integrações Windows de leitura/execução isolada. Regressão dos 101 testes anteriores incluída. Runner com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.

Os testes de armazenamento verificam primeira execução, round-trip, campos opcionais, limites, enum/IDs inválidos, duplicatas, ausência de schemaVersion, UTF-8/BOM, corrupção, backup válido/ausente/inválido, arquivo grande, versão incompatível preservada byte a byte em save/reset/close, recuperação explícita com arquivo original arquivado, serialização de 20 gravações, duas instâncias, lease de arquivo, modificação externa durante gravação, cancelamento, permissão negada e falha de substituição simuladas. Diretórios temporários são exclusivos e a limpeza verifica sua raiz antes da remoção.

Restauração de áudio usa endpoints simulados para prévia somente de leitura, confirmação explícita, geração/ID, reescolha do mesmo alvo, suporte ausente/mono, campos opcionais, mudanças externas após prévia, pedido enfileirado cancelado, desconexão e falha parcial com estado real retornado. Configuração carregada, seleção e consulta inicial preservam a contagem de escritas em zero. O código verifica também a confirmação finita do volume retornado pelo driver.

Regressões da revisão exercitam falha terminal do worker, indisponibilidade/freshness, retomada durante leitura com o mesmo ID, coalescência distinta para metadados e ciclo de vida, recuperação de falha latched na UI com serviço simulado, padrão inativo com fallback e reconciliação do mixer pedida durante leitura. Janela é ajustada a monitor disponível e testa remoção de monitor, dimensões inválidas e área útil reduzida.

Integrações da janela com armazenamento isolado executam dois ciclos de abertura/fechamento preservando seleção explícita ausente e modo automático independente. Também verificam fechamento com original corrompido ou versão incompatível, preservando bytes e backup. A execução utiliza consultas Core Audio e arquivos temporários. As escritas de áudio permanecem em simulações.

Amostra específica de medidores reais observou 20 snapshots em 2,21 s, picos zero em reprodução/captura, inscrição ativa e média de consulta de 0,090 ms. Quatro sessões expuseram pico zero na leitura pontual. O processo de testes completo observou delta de CPU de 46,9 ms e working set de 61.886.464 a 63.168.512 bytes. São dados desta amostra e máquina.

O executável foi iniciado com --settings-file temporário. Acessibilidade reconheceu a seleção ausente, controles indisponíveis, aba Preferências locais e ações de salvar/restaurar. O teste final de primeira execução gerou JSON no fechamento normal, e o reinício confirmou Preferências carregadas pela acessibilidade. A janela foi encerrada normalmente. A captura retornou window capture timed out e o clique retornou coordinate input geometry is unavailable. A validação visual e do fluxo interativo completo permanece pendente. Integrações STA cobrem reinício, estado da janela e proteção no fechamento.

### Aceitação manual da persistência

1. Anotar níveis, mute, balanço e padrões existentes no Windows e nos aplicativos. Usar sinal em nível confortável e restaurar os valores originais após ensaios de escrita. Para testar armazenamento/corrupção, usar --settings-file em pasta temporária, conforme README.
2. Escolher reprodução/captura independentemente e alternar seguir padrão. Fechar e reabrir. Conferir IDs/modos e posição/tamanho. Repetir com um ID ausente e confirmar retorno ao reconectar o mesmo dispositivo.
3. Usar Salvar valores atuais. Conferir valores salvos e atuais separados. Alterar níveis pelo Windows e atualizar. Conferir que os salvos continuam iguais e a UI mostra os níveis novos.
4. Solicitar Restaurar valores salvos, conferir alvo, prévia e aviso de aumento, cancelar e comparar os níveis. Depois, com autorização para escrita física e valores originais anotados, confirmar e comparar com o Windows. Repetir mute, balanço e mono/suporte ausente.
5. Durante a prévia, alterar um nível pelo Windows ou desconectar o dispositivo. Conferir rejeição/erro e nova consulta. Em troca rápida ou reescolha do mesmo ID, solicitar nova confirmação. Conferir estado real após qualquer falha parcial.
6. Na pasta temporária, gerar duas configurações válidas para formar backup. Com o app fechado, tornar o principal inválido, reabrir e conferir defaults em memória e original intacto. Recuperar backup por confirmação e conferir arquivo corrompido arquivado. Repetir com backup ausente/inválido e redefinição explícita.
7. No arquivo temporário, mudar schemaVersion para uma versão futura com o app fechado. Reabrir, interagir, fechar e comparar os bytes do original. Conferir aviso e ações de gravação bloqueadas. Abrir duas instâncias do mesmo arquivo temporário e conferir conflito ao gravar a segunda após mudança pela primeira.
8. Repetir fechamento durante gravação, retomada, atualização manual e reconexão rápida com o mesmo ID. Conferir recuperação dos controles, status de falha/atraso e ausência de picos antigos apresentados como atuais.
9. Validar layout/teclado, botões e diálogos em 1366×768 e DPI 100/150/200%, além de Windows 11 x64. Executar a matriz física anterior para áudio ativo, sessões e consumo prolongado.

Preferências duráveis de sessões e medidores contínuos de sessão permanecem pendentes, com motivos registrados na arquitetura. F1-I6 aguarda autorização para a etapa de validação.

## Histórico F1-I4 — resultados

Restore locked aprovado e build Debug x64 com zero erros/avisos. Suíte com 101 aprovados, zero falhas e zero ignorados, incluindo os 77 anteriores. São 95 casos de regras, simulações e grid WinForms STA e seis integrações Windows de leitura, usando TESTINGPLATFORM_TELEMETRY_OPTOUT=1.

As regressões cobrem pico zero/intermediário/máximo, NaN/infinito/fora da faixa, indisponibilidade, coalescência de duplicatas/rajadas com prazo máximo, reutilização de fontes, troca e revisão de seleção, ausência/retorno, recuperação após falha, inscrição indisponível e fallback, pausa de medição oculta, descarte idempotente e callback tardio. Worker bloqueado em leitura aguarda a operação e descarta fontes na mesma thread. Preferências testam padrão por direção, seleção explícita ausente e retorno ao modo automático. Grid STA testa identidade de linha/célula, edição/cancelamento, binding e snapshot atrasado na reescolha do mesmo endpoint. Sessão inacessível na abertura ou leitura da chave antes do alvo válido tem regressão própria, preservando os testes de erro do alvo e identidade antes de escrever.

Integração de leitura abriu medidores reais e inscrição de notificações. Foram observados 20 snapshots em 2,21 s, picos de reprodução/captura em zero, detecção automática ativa e consulta média de 0,083 ms. Delta de CPU do runner completo de 31,2 ms, working set inicial/final de 59.949.056/61.140.992 bytes. Cinco sessões expuseram medidor, todas com pico zero na leitura pontual. A amostra mede o ambiente local e inclui o runner. Escritas automatizadas continuam em simulações.

O executável abriu e encerrou normalmente. Acessibilidade reconheceu barras reais, textos de silêncio, opções seguir padrão e detecção automática ativa. Captura visual retornou window capture timed out nas duas tentativas. Os testes STA verificam comportamento da grid. Validação visual da aplicação permanece pendente.

### Aceitação física da F1-I4

1. Anotar volume, mute, balanço e padrões multimídia/comunicações antes dos ensaios e restaurar ao final. Comparar abertura e atualização com os níveis existentes no Windows.
2. Reproduzir áudio em nível confortável e falar no microfone por ação do proprietário. Conferir resposta independente dos picos e retorno ao silêncio. Comparar dispositivo selecionado e origem do sinal. Conferir indisponibilidade em endpoint inativo.
3. Selecionar explicitamente USB, removê-lo e reconectá-lo. Conferir preferência preservada, controles/mixer desabilitados durante ausência e recuperação do mesmo ID. Repetir trocas rápidas durante leitura e edição, inclusive reescolha do mesmo ID.
4. Em modo seguir padrão, alterar padrão multimídia de reprodução e captura separadamente pelo Windows. Conferir somente a direção correspondente. Alterar padrão de comunicações e conferir marcas reais. Repetir com preferência explícita e observar sua preservação.
5. Por ação do proprietário, suspender e retomar a máquina. Conferir nova inscrição/consulta, barras, mixer, níveis existentes e encerramento estável. Repetir USB e atualização manual após retomada.
6. Abrir dois aplicativos reproduzindo, conferir volume/mute por instância, mudanças externas, foco e rolagem durante polling, edição numérica, Enter/Esc, criação/encerramento e sessões inacessíveis. Restaurar valores anteriores. Repetir os passos físicos dos Incrementos 2 e 3.
7. Alternar abas/minimizar e conferir retomada dos medidores, ausência de flicker e edição estável. Verificar consumo e estabilidade por período prolongado, com diversos dispositivos/sessões. Fechar durante eventos/operações.
8. Validar rolagem e controles em 1366×768 e DPI 100/150/200%. Executar em Windows 11 x64. Windows 10 continua experimental.

Medidores contínuos por sessão aguardam avaliação de ciclo de vida e custo. A integração atual valida leitura pontual dessa API. Os cenários físicos acima permanecem pendentes.

## Histórico F1-I3 — resultados e aceitação pendente

Restore locked e build Debug x64 aprovados, com zero erros/avisos. Suíte final com 77 aprovados, zero falhas e zero ignorados, incluindo os 56 anteriores. São 73 casos de simulações/regras e 4 integrações Windows de leitura. Telemetria MTP desativada na sessão.

Novos testes verificam volume 0/100% e frações, valores inválidos, mute/unmute, abertura e refresh sem escrita, mudanças externas, instâncias do mesmo PID, sons do sistema, identidade alterada antes da escrita, PID reutilizado, endpoint divergente, sessão encerrada/removida, novas sessões, acesso negado, falhas parciais de leitura, descarte e troca de seleção com comando enfileirado. Regressões da revisão cobrem 2.000 seleções, descarte durante operação e balanço com alteração externa detectável.

Integração de leitura no endpoint padrão encontrou quatro sessões inativas, zero avisos, incluindo duas instâncias do mesmo PID e sons do sistema. Em dez leituras completas foram observadas médias de 1,40 ms no endpoint e 1,57 ms no mixer. Valores são amostras de tempo de parede desta máquina. Ausência de sessões continua sendo resultado válido.

O executável abriu e as abas foram reconhecidas por acessibilidade. Captura visual continuou retornando timeout. A navegação automatizada não confirmou visualmente a lista do mixer. Layout e operações físicas permanecem pendentes.

Para aceitação física, o proprietário deve abrir dois aplicativos com reprodução no mesmo endpoint e anotar/restaurar os níveis originais. Conferir nome, volume numérico com Enter/Esc, mute/unmute e isolamento entre sessões e endpoints. Alterar volume pelo Windows e conferir atualização, iniciar/encerrar aplicativos, trocar rapidamente de saída, remover USB e encerrar durante operação. Conferir arraste contínuo, roda do mouse e teclado dos sliders de endpoint e rolagem em 1366×768 e DPI 100/150/200%. Validar Windows 11 x64. Windows 10 segue experimental. As escritas dos testes automatizados usam simulações.

As seções seguintes preservam verificações e critérios dos incrementos anteriores e o planejamento das fases posteriores.

## Verificação do Incremento 2

O Incremento 1 foi publicado no commit 60238ea. O Incremento 2 recebeu aprovação provisória para publicação e revisão técnica. A suíte inclui a regressão dos 16 testes anteriores. Os testes de escrita usam endpoints simulados, preservando os níveis físicos do host. A nova integração Windows faz leitura dos controles reais e descarte dos wrappers/callbacks.

Verificação final — `dotnet restore WaveStudio.slnx --locked-mode` aprovado, `dotnet build WaveStudio.slnx -c Debug --no-restore` com zero erros/avisos e `dotnet test --solution WaveStudio.slnx -c Debug --no-build` com 56 aprovados, zero falhas e zero ignorados. São 53 casos de regras/simulações e 3 integrações Windows de leitura. A execução usou `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`.

Casos automatizados novos cobrem 0/100% e frações, rejeição de limites/NaN/infinito, mute/unmute, centro/extremos/intermediários, repetição sem acumular atenuação, silêncio, mono, máscara ausente e multicanal, suporte parcial, driver ignorando escrita, canais acoplados, restauração após falha e cancelamento, desconexão durante balanço, alterações externas simuladas, mudança de seleção, geração antiga para o mesmo ID, comandos enfileirados cancelados, isolamento de entrada/saída e descarte.

A integração real leu volume 100% e mute desligado dos endpoints padrão do host, com L/R 1/1 na reprodução e balanço desabilitado no microfone mono. A janela abriu e esses controles foram confirmados por acessibilidade. Captura de tela falhou por timeout. O layout e as operações físicas de escrita continuam pendentes.

## Passos físicos do Incremento 2

Antes de alterar níveis, anotar volume master, mute e níveis de cada canal dos endpoints de entrada/saída. Usar volume confortável e sinal de teste de baixa intensidade. Restaurar os valores anotados ao terminar, incluindo balanço e mute. Esses ensaios exigem ação explícita do usuário e hardware disponível.

1. Abrir o app e comparar os valores com os controles do Windows, confirmando que a abertura e a seleção mantêm níveis e padrões existentes.
2. Ajustar volume de reprodução pelo slider e pelo número. Conferir o valor no Windows. Repetir para captura quando disponível. Testar 0/100% em condições controladas, sem sinal alto ou monitoramento que possa causar feedback.
3. Ativar/desativar mute em cada direção e conferir isolamento, valor anterior de volume e estado no Windows.
4. Em estéreo com L/R confirmado, testar centro, esquerda, direita e posições intermediárias com sinal de canais conhecido. Conferir que o maior nível de canal preserva a base e que centralizar recupera ambos nessa base.
5. Selecionar microfone mono e endpoint sem mapeamento L/R. Conferir balanço desabilitado com motivo e demais controles funcionando conforme suporte. Caso um driver acople canais, conferir erro e tentativa de restauração.
6. Alterar volume/mute e canais pelo Windows. Aguardar aproximadamente um segundo e conferir atualização na UI sem oscilação ou reaplicação de valores anteriores.
7. Trocar rapidamente de dispositivo durante uma leitura/edição e conferir que a nova seleção mostra seus próprios níveis e recebe somente ações realizadas para ela.
8. Remover um USB durante ajuste. Conferir erro, controles desabilitados e atualização de lista. Reconectar e tentar novamente. Conferir e restaurar canais no Windows se uma operação de balanço tiver sido interrompida.
9. Fechar durante consulta ou comando, reabrir e conferir estabilidade e níveis existentes. Avaliar escala 100/150/200% e repetir em Windows 11 x64 suportado.

Registrar resultados por dispositivo/driver. As chamadas de balanço são sequenciais, e desconexão física pode impedir a restauração automática. O comportamento de modo exclusivo e o suporte de escrita por driver permanecem sujeitos a validação física.

## Resultados do Incremento 1

- `dotnet restore WaveStudio.slnx` passou após repetir com acesso autorizado ao NuGet fora do sandbox. `dotnet restore WaveStudio.slnx --locked-mode` também passou.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou com zero erros e zero avisos.
- `dotnet test --solution WaveStudio.slnx -c Debug --no-build --output Detailed`, com telemetria MTP desativada na sessão, aprovou 16 testes, zero falhas e zero ignorados. Foram 14 casos de regras/mensagens e 2 testes de integração Core Audio.
- A integração enumerou 67 endpoints reais, com zero avisos. Observou estados ativo/ausente/desconectado, padrão de reprodução multimídia diferente do de comunicações, captura padrão e formatos de mixagem. Consultas concorrentes serializadas e consulta após cancelamento também passaram.
- `WaveStudio.exe` foi iniciado. A acessibilidade da janela confirmou nome/versão, seletores, dados de entrada e saída, botão Atualizar e status de 67 endpoints. A captura de tela falhou com `FrameArrived timed out` e `window capture timed out`. O clique automatizado falhou com `coordinate input geometry is unavailable`. A conferência visual e das interações permanece pendente.

Os testes `WindowsIntegration` consultam a configuração real do host. Um inventário vazio é válido. Avisos de falha de API reprovam esses testes e exigem diagnóstico local. Para executar apenas regras, use `dotnet test --solution WaveStudio.slnx --filter-not-trait Category=WindowsIntegration`.

## Passos físicos do Incremento 1

1. Executar `dotnet run --project src/WaveStudio.Desktop`. Conferir título, versão, textos, legibilidade e navegação por teclado em 100%, 150% e 200% de escala.
2. Comparar nomes, estados e padrões de multimídia/comunicações das duas listas com as configurações de som do Windows. Dispositivos históricos podem aparecer como ausentes. Ativos aparecem primeiro.
3. Selecionar outro headset e conferir nome, ID, estado, canais, taxa e bits do mix. Confirmar que a seleção de microfone e o padrão do Windows continuam os mesmos. Repetir trocando somente o microfone.
4. Clicar em Atualizar e conferir preservação das duas seleções. Alterar um padrão explicitamente pelo Windows e atualizar para conferir sua identificação.
5. Remover um USB selecionado e clicar em Atualizar. Conferir estado ausente/desconectado ou aviso de indisponibilidade. Reconectar, atualizar e conferir recuperação pelo mesmo ID quando preservado pelo Windows.
6. Consultar um endpoint inativo. Conferir indicação de formato indisponível. Fechar a janela durante uma atualização e reabrir para conferir estabilidade.

Registrar modelo/conexão, versão do Windows e resultado por passo. Repetir em Windows 11 x64 suportado. No Windows 10 local, os resultados atuais cobrem execução experimental e consultas reais. Os passos de volume, sessões e medidores abaixo pertencem aos incrementos seguintes.

## Testes automatizados planejados

| Área | Casos mínimos |
| --- | --- |
| Configurações | Round-trip JSON, padrão, migração conhecida, versão futura, corrupção, backup, arquivo ausente, falha de escrita e gravações concorrentes serializadas |
| Volume e balanço | Limites, centro neutro, extremos com atenuação, preservação do nível-base, mono, multicanal sem L/R e suporte ausente |
| Estado de dispositivos | Preferência ausente, seguir padrão, troca de seleção, eventos repetidos, remoção durante comando e descarte idempotente |
| Sessões | Criação/encerramento, PID reutilizado, sons do sistema, múltiplas sessões, identidade indisponível e isolamento das alterações |
| Apresentação | Medidor indisponível, atualização externa sem realimentação de comandos, resposta atrasada de seleção anterior e erro recuperável |

Testes com serviços simulados verificam regras e estados. Testes de integração Windows exercitam Core Audio, com seus resultados identificados separadamente.

## Aceitação física da Alpha

Preparar volume confortável, anotar valores iniciais do Windows e aplicativos e restaurá-los ao final. Usar fones quando necessário. Registrar sistema, versão do app, dispositivo/conexão, suporte exposto, passos, resultado e evidência por caso.

| Caso | Procedimento e resultado esperado |
| --- | --- |
| T01 Enumeração | Comparar entrada/saída, IDs, estados e padrões de multimídia/comunicações com o Windows. Propriedades ausentes são informadas |
| T02 Seleção | Selecionar entrada e saída separadamente. Somente o alvo administrado muda, sem alterar padrão do sistema |
| T03 Volume/mute | Alterar no app e conferir no Windows. Alterar no Windows e conferir atualização do app. Exercitar entrada e saída |
| T04 Balanço | Usar sinal estéreo conhecido e verificar centro/esquerda/direita. Mono ou suporte ausente desabilita o controle com explicação |
| T05 Sessões | Abrir dois aplicativos reproduzindo áudio. Ajustar volume/mute de uma sessão, conferir isolamento, encerrar e reabrir o aplicativo |
| T06 Medidores | Reproduzir áudio e falar no microfone. Picos reais reagem quando disponíveis, silêncio retorna a zero, indisponibilidade é explícita |
| T07 Hotplug | Remover/reconectar USB durante leitura e mudança de volume. UI permanece responsiva e reconcilia estado sem trocar preferências silenciosamente |
| T08 Padrão/retomada | Alterar padrão no Windows, suspender/retomar e testar ausência de endpoints. Reconexão recupera serviços e controles válidos |
| T09 Persistência | Reiniciar, conferir preferências e restauração explícita. Testar JSON inválido/futuro e pasta sem escrita, preservando dados recuperáveis |
| T10 Robustez/UI | Abrir/fechar repetidamente, trocar endpoints rapidamente, usar teclado e escala 100/150/200%. Verificar erros, layout e consumo observado |
| T11 Privacidade | Conferir recursos apresentados, ausência de arquivos de áudio criados automaticamente e logs sem conteúdo de áudio |

Executar no Windows 11 x64 suportado. Registrar separadamente resultados no Windows 10 local e sua elegibilidade de suporte. Cobertura por dispositivo deve incluir USB, analógico e Bluetooth disponíveis. HDMI/DP, virtual e hardware adicional permanecem pendentes até ensaio próprio. Anotar captura bloqueada por privacidade do Windows, modo exclusivo e controles ausentes.

## Prova externa das Fases 2–3

1. Documentar integração virtual candidata, versão, licença, assinatura, instalação autorizada, restauração e desinstalação. Manter proteções do Windows habilitadas.
2. Direcionar navegador à entrada virtual, processar ganho conhecido no motor e reproduzir no endpoint físico. Comparar bypass/processado com sinal conhecido e verificar ausência de reprodução duplicada.
3. Capturar microfone físico, aplicar processamento mensurável e entregar ao endpoint de captura virtual selecionado em OBS e Discord. Confirmar sinal recebido no aplicativo externo.
4. Medir latência ponta a ponta com método descrito, além de CPU/RAM, taxa, buffers, underruns/overruns e recuperação após hotplug. Separar medição de estimativa.
5. Testar loops/mix-minus, destinos externos opt-in e restauração após encerramento normal e inesperado. Desinstalar a integração de teste quando autorizado e conferir estado anterior.

Registrar aprovação ou bloqueio de cada direção antes de ampliar o catálogo de efeitos. Testes DSP posteriores usarão senos, impulsos, silêncio, ruído, clipping, taxas e canais distintos, com comparação objetiva de ganho/resposta, estabilidade e latência.
