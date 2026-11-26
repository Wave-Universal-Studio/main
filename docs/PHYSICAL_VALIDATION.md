# Validação física integrada do Wave Studio

Rodada de 24/11/2026 no Windows 10 Pro x64, build 19045, sobre a `main` sincronizada por fast-forward até `4bab6f2f6e4c8b60f73c8f33317c23ab90531809`. SDK .NET 10.0.404 e runtime 10.0.15 x64. O inventário usa a versão Release deste commit. As evidências do proprietário e as provas antigas continuam válidas em seus respectivos escopos.

## Evidências desta rodada

| Verificação | Resultado e alcance |
| --- | --- |
| Regressão local x64 | Restore locked com auditoria de todos os pacotes e NU1900 como erro. Build Release não incremental com zero avisos e zero erros. 959 testes aprovados, zero falhas, sete Explicit excluídos, 56,941 s, seed 20261113. A CI da mesma base funcional registra 959 em Debug e 959 em Release. |
| Windows e Core Audio | Seis endpoints ativos de reprodução e três de captura. Catálogo sem avisos, com o FIFINE e o headset USB selecionados por nome exato e direção. |
| Estabilidade de leitura | 120,033 s observados, 1.200 snapshots (1.199 com ambos os medidores válidos), zero atrasados ao observar e zero falhas do worker. Intervalo p95 entre atualizações de 100,76 ms, máximo de 101,70 ms. Consulta p95 de 0,196 ms, máximo de 1,337 ms. Descarte em 2,08 ms, sem erro. CPU do processo coletor de 437,5 ms, working set de 28,4 MiB no início, pico amostrado de 32,3 MiB e 31,4 MiB ao fim. Os dois endpoints continuaram ativos. O ensaio mede leitura e descarte do serviço de medidores em repouso, sem transporte contínuo. |
| Períodos de dispositivo | Consulta WASAPI sem Initialize ou Start confirmou float32 a 48 kHz nos quatro endpoints do cabo simples. Headset e cabo estéreo, FIFINE mono. Período padrão de 10 ms em todos. Mínimo exclusivo informado de 3 ms no headset, FIFINE e captura do cabo, e 2 ms na reprodução do cabo. Buffer inicializado e latência do stream ficam indisponíveis nessa consulta. |
| Driver e boot | Driver assinado VB-Audio 3.5.2.0. Evento Kernel-Boot 27 de 24/11 às 00:19:37, UTC−03, com `BootType=0`, consistente com o horário de boot do Windows e posterior à instalação de 19/11. O evento mais recente, às 08:25:49, teve `BootType=1`. A instância ativa e saudável tem dois pinos de reprodução e um de captura. A outra instância permanece em Código 10, sem pino ativo associado. |
| Prontidão dos dois fluxos externos | Cabo simples disponível. Pareamento e saúde dos quatro alvos confirmados. Os seis padrões do Windows estão fora do cabo. Navegador → cabo → headset e microfone → cabo → OBS passaram na consulta de compatibilidade. A+B ausente. OBS portátil 33.0.1 existente com assinatura Authenticode válida de OBS Project, LLC. O SHA-256 do WAV local de 1 kHz coincide com o histórico do preparo. |
| Relógios, reconexão e áudio das rotas | A descoberta por filtro exato encontrou um método Explicit para a coleta de clock em razão nominal e um para a prova sequencial do cabo simples. Na primeira rodada houve somente consulta de capacidades e leituras de metadados e medidores. A autorização posterior e o resultado do novo preflight estão na seção de continuação. |

O tempo de atualização do medidor e o período de dispositivo são grandezas diferentes de latência acústica. Na preparação histórica parada do FIFINE e headset, o Windows retornou buffer de 1.056 frames por lado e `GetStreamLatency=0` com ambos os streams parados. O retorno não mede o atraso ponta a ponta. A prova acústica precisa de um estímulo conhecido e de referência de tempo comum, com captura da resposta e distribuição de atrasos. [Contrato de latência do WASAPI](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getstreamlatency).

## Continuação autorizada em 24/11/2026

A preparação usou um worktree separado, fixado em `9b12b72` e com a mesma base funcional Release `4bab6f2`. Os hashes da cópia do apphost e das DLLs de testes, áudio e desktop coincidiram com os binários usados na regressão de 959 testes. A descoberta no worktree encontrou exatamente o método Explicit de clock e o de cabo simples pelos filtros exatos. Provas anteriores e regressão ficaram preservadas.

| Verificação | Evidência da continuação |
| --- | --- |
| Isolamento da rota | O preflight do próprio runner confirmou os quatro alvos ativos e o pareamento exato. Os padrões permaneceram fora do cabo, sem produtor ativo nos pinos de render do VB-CABLE no momento da consulta. |
| Preservação | Os snapshots locais antes e depois coincidiram nos seis papéis de dispositivo padrão e no volume, mute e balanço disponíveis dos nove endpoints ativos. Os binários, dados de dispositivos e snapshots ficam fora do versionamento. |
| Coleta de clock | **Bloqueada antes de Start.** Leituras de ocupação encontraram duas sessões de captura ativas no microfone selecionado. O runner autorizado ficou preparado para uma coleta de até 10 s, em razão nominal, com saída SILENT e sem persistência de áudio. |
| Reconexão USB | **Aguardando ação física.** A observação usa o medidor Core Audio e a seleção original, preparada para registrar remoção, retorno do endpoint, recuperação do medidor e comparação dos seis padrões, sem abrir um stream. |
| Navegador e OBS | **Aguardando operador e condições de áudio.** O controle visual de janelas estava indisponível nesta sessão. O runner exige confirmação pessoal no console, aplicação observável das rotas temporárias, ausência de transmissão e verificação da restauração. |

A autorização desta etapa foi registrada, mas a janela física ainda depende de microfone livre e presença do operador para os atos físicos e a confirmação local. As sessões em andamento ficaram abertas. Nenhuma tentativa Explicit foi consumida e a aceitação física das etapas permanece em aberto. O escopo da Fase 5 continua sendo o caminho interno validado em simulação.

## Retomada em 26/11/2026

A `main` foi sincronizada até `1f7caf6` por fast-forward, com árvore local limpa. A base foi recompilada em Release x64 com restore locked e auditoria NuGet, zero erros ou avisos. A regressão local teve 959 aprovados, zero falhas e sete Explicit excluídos, em 60,123 s. As medições físicas usaram o apphost Release da mesma base funcional num worktree isolado.

| Prova | Resultado desta janela |
| --- | --- |
| Mixer real do Windows | **Aprovado para controle de sessão.** Duas instâncias de teste criaram sessões WASAPI ativas, distintas, no headset e reproduziram exclusivamente zeros digitais. Só as sessões de teste receberam comandos. O mute de uma preservou o da outra. O jogo permaneceu em 70% quando a comunicação caiu de 50% para 25%. Centralizar retornou a 50%. Um ajuste de terceiros simulado por um comando real de sessão a 35% foi preservado pela restauração condicional. Volume e mute originais foram conferidos em ambas as sessões antes de encerrá-las cooperativamente. Esse ensaio confirma o backend e o readback do Windows. O gesto na mesa e o efeito audível com aplicativos de usuário continuam em aberto. |
| Reconexão USB | O medidor ficou armado em duas janelas de até 125 s, com a seleção explícita do headset e mantendo o microfone conectado. Nenhuma janela registrou remoção. O worker não falhou e os seis padrões do Windows coincidiram após cada janela. A recuperação permanece inconclusiva. |
| Sincronização de clocks | A ocupação do FIFINE caiu de duas sessões no registro anterior para uma sessão ativa na retomada. A leitura imediatamente anterior ao Start e a leitura após a segunda janela USB ainda encontraram uma sessão de captura ativa. A coleta de 10 s ficou bloqueada antes de Start. |
| Latência real e estabilidade do transporte | Sem nova medida. A leitura de período do dispositivo e as duas fontes silenciosas do mixer não medem latência ponta a ponta nem estabilidade de um transporte com captura. |
| Navegador → headset e microfone → OBS | O pareamento em leitura e a aceitação manual anterior foram mantidos como evidências. A execução com áudio, o retorno ao estado original e a instrumentação dos dois fluxos continuam pendentes. |

Os logs e os JSON agregados ficam no armazenamento local ignorado pelo Git. O teste do mixer não abriu o microfone, não produziu som audível e encerrou as duas fontes com sucesso. O relatório e os commits contêm apenas resultados técnicos.

## Reconexão assistida do headset em 26/11/2026

**Aprovada para seleção explícita e medidor.** O Windows registrou a remoção do headset USB durante a observação, com o microfone conectado. O medidor da saída ficou indisponível em 343,7 ms após a detecção da remoção. A reconexão na mesma porta trouxe de volta o mesmo endpoint e a leitura do seu medidor em 167,0 ms após a detecção do retorno. O worker registrou três notificações de catálogo, encerrou sem erro e preservou os seis padrões de áudio do Windows. Os tempos descrevem a observação amostrada, com consultas do catálogo a cada aproximadamente 150 ms. A prova verificou o medidor e a seleção, sem abrir um transporte PCM.

A primeira consulta após a reconexão ainda indicou uma sessão de captura ativa da NVIDIA. Após o operador desativar toda a sobreposição, a consulta imediatamente anterior à coleta encontrou **zero sessões de captura ativas** no microfone. O mixer já aprovado foi reaproveitado.

## Coleta silenciosa dos relógios e do transporte em 26/11/2026

Uma tentativa Explicit usou a versão Release da prova no worktree físico, com captura FIFINE e render no headset USB, razão nominal e saída exclusivamente SILENT. Os dois relógios progrediram com continuidade corroborada pelos pacotes. A coleta terminou em **10,0008986 s** de QPC, com 997 pacotes de captura aceitos, 478.560 frames escritos e 476.160 lidos da fila, zero drops e zero underruns. Stop e Release foram confirmados nos dois lados, sem recursos retidos. Nenhuma voz foi armazenada.

**Critério de drift não atingido.** Houve 89 cortes coerentes e 26 janelas estimáveis, distribuídas por 2,755135 s, abaixo dos 4 s exigidos pelo teste. O último intervalo de correção foi de −12.804,70 a +12.971,78 ppm, inclui zero e não permite concluir magnitude ou necessidade de correção. O teste terminou com código 2 e nenhuma correção foi aplicada. Essa execução confirma transporte nominal e continuidade na janela curta. Estabilidade prolongada e recuperação do transporte ainda precisam de evidência própria.

`GetStreamLatency` retornou zero nos dois endpoints após Start. A medição acústica ponta a ponta continua pendente de estímulo conhecido e referência comum. [Contrato de latência do WASAPI](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getstreamlatency).

## Prontidão do navegador e do OBS em 26/11/2026

Após o retorno do headset, o preflight em leitura confirmou os quatro endpoints ativos, pareamento do cabo simples, padrões do Windows fora do cabo e ausência de outros produtores ativos nos pinos de render consultados. O estímulo sintético local e o OBS portátil já verificados foram reaproveitados na preparação. Com silêncio digital no perfil isolado do navegador, o operador verificou a saída original **Padrão**. A cópia isolada do OBS abriu com uma cena vazia, sem fontes globais ou de captura. Antes e depois da preparação, o FIFINE estava sem sessões ativas. Os seis padrões, volumes, mutes e balanços disponíveis permaneceram iguais. Nenhuma rota temporária foi aplicada. O snapshot do runner atual distingue padrão e endpoint explícito, mas não representa uma fonte original desativada no OBS. As fases instrumentadas não foram iniciadas. A aprovação manual anterior dos dois fluxos continua registrada.

## Pendências por fase

| Fase | Evidência disponível | Pendência física ou de aceitação |
| --- | --- | --- |
| F0 e distribuição Alpha | Build e testes locais no Windows 10. Pacote e abertura/fechamento de versões anteriores documentados. | Validação do pacote portátil atualizado em Windows 11 x64, inclusive abertura, encerramento, ícone na barra de tarefas/Alt+Tab e ausência de dependências externas inesperadas. Windows 10 permanece plataforma experimental. |
| F1 · dispositivos, medição e recuperação | A01, A02, A03, A05, A09, A11 e A12 aprovados por relato pós-formatação. A leitura atual é evidência adicional. | Reconexão assistida com seleção explícita e recuperação do medidor aprovada nesta rodada. Ainda faltam modo Seguir padrão, suspensão/retomada e encerramento sob atividade. Matriz de dispositivos compatíveis, incluindo USB ensaiado, analógico, Bluetooth e HDMI/DP quando disponíveis. Conferir situações de captura bloqueada e controles de balanço ausentes quando houver ambiente pertinente. |
| F1 · mixer, preferências e interface | Regressão de sessões, persistência, recuperação de arquivos isolados, foco e layout. A14 perfis de EQ aprovado por relato geral. | A04, isolamento de volume/mute com duas sessões reais. A06, salvar e restaurar níveis com confirmação e conferir após reabrir. A07, aceite assistido com arquivo descartável, inclusive recuperação e proteção de esquema futuro. A08, teclado, rolagem e DPI 100/150/200%. A10, Windows 11 x64. A13 tem diferença audível relatada. Ausência de distorção, limites, bypass e descarte merecem confirmação de subcasos ainda sem evidência. |
| F2 · transporte, sincronização e escuta | Prova 4 confirmou 5 s silenciosos, movimento, continuidade e liberação. A prova nominal SILENT terminou em 4,999 s no par escolhido com continuidade nominal, zero drops/underruns e liberação confirmada. A escuta da própria voz foi confirmada pelo proprietário, sem JSON nem código de saída recuperados. A coleta histórica de drift encerrou aos 3,722 s por `WindowRateOutsideTolerance`. A nova coleta nominal concluiu 10,001 s com zero drops e underruns. Houve janelas estimáveis, com separação insuficiente para o critério de drift. | Medir relógios independentes em janela longa o suficiente, verificar a necessidade de compensação e confirmar integridade da fila, Stop/Release, latência acústica e recuperação real do transporte após perda do dispositivo. Monitoramento contínuo de produção e correção física de drift aguardam a implementação/validação de seus caminhos próprios. O runner de escuta atual opera por até 5 s. |
| F3 · roteamento externo | O proprietário confirmou manualmente os dois fluxos. A consulta desta rodada confirmou a prontidão do cabo único, formatos e padrões. | Prova instrumentada do navegador com amplitude antes/depois, quietude na passagem para o microfone, medidor da fonte exata no OBS, ausência de loop/duplicação, contadores por fluxo e restauração das duas seleções. A+B, simultaneidade e recepção no Discord continuam sem evidência física. A+B requer dois adaptadores disponíveis e confirmação específica. |
| F4 · mesa, Game/Chat e idiomas | Faders, master e balanço de sessão, conflitos, restauração e internacionalização cobertos pela regressão. Validação de volume, mute, Game/Chat e restauração em duas sessões WASAPI reais e silenciosas nesta rodada. | Exercitar dois aplicativos reais no mesmo endpoint, atribuir Jogo/Comunicação, confirmar atenuação e centro contra níveis do Windows, conflito por ajuste externo, troca de grupo, reconexão e fechamento. Conferir as três línguas e o layout no desktop real se houver discrepância com o teste STA. Solo foi adiado. |
| F5 · barramentos internos | Matriz Personal/Stream, ponte PCM, passos e agendador MTA. A F5-I2C4 liga leituras `IAudioClock` dos owners preparados ao corte do scheduler, com verificação automatizada usando substitutos de COM. | Coordenar preparação, Start, Stop e recuperação dos clientes pelo owner. O caminho físico contínuo, a segunda saída, a sincronização entre dispositivos e a integração desses barramentos com OBS/Discord ainda não estão disponíveis para aceitação. |

Os critérios completos e o histórico estão nos roteiros da [Alpha](ALPHA_ACCEPTANCE.md), do [backend WASAPI](WASAPI_BACKEND_PLAN.md), do [roteamento externo](EXTERNAL_ROUTE_PROOF.md) e no [plano de testes](TEST_PLAN.md).

## Reprodutibilidade e privacidade

| Evidência numérica local | SHA-256 |
| --- | --- |
| `native-preflight.json` | `604a00849b7957fbb31306b83bfdf590e9b7001e5ac714405136f6610f4d4e19` |
| `readonly-soak.json` | `af4ac8a6c130647b67f46a3f5d72129619f139e134b6f08ab13a1c33ea61bb0e` |
| Agregado da coleta SILENT de 10 s de 26/11 | `ec5628ad38918f30406166fbbc916afe17665b84c1a39ca42f62e0b9275bcd87` |

```powershell
dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900
dotnet build WaveStudio.slnx -c Release --no-restore --no-incremental -warnaserror --nologo
dotnet test --solution WaveStudio.slnx -c Release --no-build -- --explicit off --seed 20261113 --timeout 165s --long-running 10 --diagnostic --diagnostic-output-directory artifacts/physical-validation-20261124/diagnostics-Release
```

A consulta usa `CoreAudioDeviceService`, `ExternalRouteReadinessService` e `WasapiEndpointProbe` da mesma DLL Release. A observação de estabilidade usa `AudioWatchWorker` em leitura dos medidores Core Audio, com amostragem do estado publicado, conferência da seleção ao final e descarte cooperativo. Os logs, os resumos numéricos e o código do coletor local ficam sob `artifacts/physical-validation-20261124`, ignorado pelo Git. Esta documentação publica somente os resultados agregados, sem IDs dos dispositivos, caminhos de perfis, amostras PCM ou voz.

A medição tem escopo de evidência local em Windows 10. Para iniciar áudio ativo ou editar rotas dos aplicativos, é necessária confirmação das condições naquele momento. A restauração preserva valores externos observáveis e confirma as seleções originais antes de considerar encerrado o lote.
