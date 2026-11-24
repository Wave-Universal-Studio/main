# Changelog

## Em desenvolvimento

### 2026-11-24 — F5-I2B-NATIVE-PCM-PACKET-ADAPTER (em revisão)

- Adaptação de pacotes PCM16/float32 WASAPI para blocos float32 do mixer e codificação inversa com saturação segura.
- Validação de flags, formato, endpoint/geração, bytes e frames; silêncio explícito e tickets de saída revalidados.
- Oito testes novos com dados sintéticos, sem abrir dispositivos ou iniciar captura; runtime WASAPI contínuo ainda pendente.

### 2026-11-24 — F5-I2A-BOUNDED-PCM-BRIDGE (integrada)

- Adicionada ponte de transporte PCM com filas SPSC limitadas por captura, Personal Mix, Stream Mix e destino externo autorizado, sem abrir áudio nativo.
- Confiabilidade: identidade endpoint/geração, slots 10 ms, backpressure, descarte de blocos obsoletos e tickets válidos até o ponto de publicação.
- Seis novos testes sem dispositivos físicos. Integração real WASAPI permanece para etapa posterior.

### 2026-11-24 — F5-I1-BUS-MATRIX-CORE

- Motor PCM float32 de dois barramentos com matriz de envios autorizados, ganho e mute independentes, rampas, proteção contra clipping e medição de pico/RMS.
- Transporte injetado com blocos sincronizados, adaptação mono/estéreo, conversão nominal 44,1/48 kHz, descarte de blocos obsoletos e ticket por geração.
- Stream Mix com destino externo desativado por padrão, autorização explícita de fontes sensíveis e exclusão do retorno da mesma chamada.
- Workflow de regressão Windows x64 em Debug e Release. Os dois barramentos permanecem internos nesta entrega.

### 2026-11-24 — F4-I2-GAMECHAT-SOLO-LANG

- Adicionado balanço proporcional entre sessões Windows ativas atribuídas a Jogo e Comunicação, com restauração condicional, leitura de confirmação e aviso de conflito.
- Adicionados os catálogos completos de inglês (Estados Unidos) e espanhol (Espanha), com preferência persistente, Seguir Windows e fallback em português brasileiro.
- Solo ficou fora da entrega porque o mute compartilhado de sessões no Windows pode atingir chamadas e sua restauração não é atômica.

### 2026-11-23 — F4-I1-REAL-MIXER

- Mesa com faders, mute e pico por sessão real, master do dispositivo, filtro por grupos locais e a grade em Detalhes das sessões.
- Configurações → Idioma com recursos pt-BR, opção Seguir Windows e migração das preferências locais para o esquema 3.
- A escrita do mixer confirma a identidade e o estado observado. A troca de dispositivo ou o fechamento cancela edições pendentes.

### 2026-11-19 — F3-I3-ROUTE-READINESS-DESKTOP

- Adicionada a aba experimental de roteamento externo com inventário, seleções locais, diagnóstico do VB-CABLE simples e disponibilidade A+B.
- Integrados os estados por fluxo e as orientações manuais. A consulta atualiza com mudanças de dispositivos, descarta resultados obsoletos e participa do fechamento cooperativo.

### 2026-11-19 — F3-I2-POSTBOOT-PAIRING

- Corrigido o pareamento do VB-CABLE simples para usar a instância PnP do dispositivo conectado. Pinos adicionais, driver esperado e estado PnP são verificados antes de abrir a rota.
- A pré-verificação somente leitura passou no par instalado. O Código 10 permaneceu em outra instância PnP e a prova física segue pendente.

### 2026-11-19 — F3-I2-SINGLE-CABLE-PROOF

- Verificados os pacotes oficiais e instalado o driver VB-CABLE simples 3.5.2.0 com autorização. OBS 33.0.1 preparado em modo portátil isolado.
- Conferidos padrões e níveis do Windows. A pré-verificação rejeitou o pareamento da rota antes de captura ou reprodução. A prova agrupada segue pendente.

### 2026-11-19 — F3-I2-SINGLE-CABLE

- Preparado modo de avaliação por cabo único, com fases sequenciais, consentimentos por fase, identidades distintas por geração e restauração verificável.
- A troca entre navegador e OBS exige quietude do cabo, ausência de produtor ativo e Stop/Release confirmados. O grafo A+B mantém os dois adaptadores independentes.
- Preparado runner Explicit agrupado e roteiro de revisão, instalação e prova futura. Nenhuma compra, instalação ou captura física foi executada.

### 2026-11-19 — F3-I2-EXTERNAL-ACCEPTANCE

- Conferidos o Release x64 existente e as dependências da prova externa. A disponibilidade do A+B e do OBS ainda precisa de confirmação.
- Definidos os critérios de aceitação da sessão agrupada, o estímulo sintético local e a restauração independente do runner. A execução física aguarda a escolha de dependência e autorizações específicas.

### 2026-11-19 — F3-I1-EXTERNAL-ROUTE

- Rota WASAPI opt-in e limitada em dois fluxos sequenciais, com processamento no motor existente, validação de identidade dos pares e parada cooperativa.
- Comparação de bypass seguro com atenuação fixa, telemetria numérica por patamar e contrato de configuração e restauração manual com detecção de conflito.
- Preparada a aceitação agrupada com VB-CABLE A+B e OBS. A integração física e o fechamento da Fase 2 aguardam revisão e autorização específica.

### 2026-11-19 — F2-I6D-AUDIBLE-PROOF

- O proprietário confirmou escuta da própria voz no headset e funcionamento do monitoramento. A aprovação manual foi registrada no escopo da escuta.
- Os artefatos disponíveis documentam o preparo do Release x64 e a janela que encerrou antes do apphost. O resultado automatizado da escuta relatada segue indisponível. A prova e os testes anteriores não foram repetidos.

### 2026-11-18 — F2-I6D-ALLOC-MONITOR

- Reproduzida no .NET 10.0.15 a supercontagem de alocações por GC em segundo plano. Os quatro ensaios históricos preservam as medições exatas e os limites originais, em processos separados das outras classes, com controles de zero e de alocação conhecida.
- Preparada a prova WASAPI audível nominal de até cinco segundos, com headset confirmado, ganho inicial zero, teto fixo de 0,05 e parada pelo operador. A execução em hardware aguarda revisão e autorização específica.

### 2026-11-18 — F2-I6C-NOMINAL-PROOF

- A tentativa física nominal SILENT concluiu o temporizador de cinco segundos e foi aprovada no escopo do transporte nominal. Foram observados 4,9991228 s por QPC, 191 cortes coerentes, zero perdas de FIFO e liberação confirmada.
- A evidência do clock de captura ficou temporariamente indisponível em uma leitura, sem invalidar a continuidade dos pacotes. Correção de drift e monitoramento audível seguem fora do escopo aprovado.

### 2026-11-18 — F2-I6C-NOMINAL-MONITOR

- Transporte nominal opt-in de até cinco segundos, com verificação independente de pacotes, margem de FIFO e relatório separado de drift.
- Render Float32/PCM16 com ganho digital nos fakes. Preparada prova nominal nativa SILENT, sem execução física.

### 2026-11-18 — F2-I6B2B-DIAG-PROOF

- A tentativa diagnóstica nominal SILENT encerrou em 3,7220736 s por rejeição da janela móvel do clock de captura. Os dois streams confirmaram Stop e liberação.
- Registrados 20 cortes coerentes e zero janelas de drift estimáveis. Correção física desativada, sem voz armazenada. A coleta não foi aprovada e segue para revisão.

### 2026-11-18 — F2-I6B2B-DRIFT

- Ponte de clocks e pacotes sobre cortes coerentes, controle intervalar exercitado no caminho WASAPI com fakes e preenchimento da FIFO separado do priming do endpoint.
- Preparada coleta física diagnóstica SILENT de dez segundos em razão nominal, com autorização própria e relatório sanitizado. A ativação do controle no hardware depende de evidência adicional.

### 2026-11-18 — F2-I6B2A-CHECKPOINT

- Diário WASAPI opt-in com contabilidade em frames de saída, confirmação de corte dos dois owners e limites conservadores para ocupação média.
- Cobertura automatizada com fakes para concorrência, wrap, contabilidade, erros nativos, cancelamento e retenção do guard. A prova silenciosa existente mantém seus critérios.

### 2026-11-18 — COMENTARIOS-PTBR

- Traduzidos 209 comentários próprios em 74 arquivos C#, PowerShell e XML. A verificação por parser preservou tokens, sintaxe, strings e diretivas.
- Preservadas a aceitação manual registrada e a prova silenciosa 4. Checkpoint textual para revisão antes de F2-I6B2.

### 2026-11-18 — F2-I6B1-PROOF-4

- Prova física silenciosa aprovada pelo teste, com temporizador de 5 s concluído e 4,9997441 s medidos por QPC. Confirmadas janelas de clock, continuidade de 499 pacotes e corroboração terminal.
- Os dois streams confirmaram Stop e liberação. Nenhuma voz foi armazenada. Código preservado e F2-I6B2 pendente.

### 2026-11-18 — F2-I6B1-CLOCK-WINDOW

- Captura da prova silenciosa com orçamento provisório ligado ao período consultado, âncora fixa, janelas móveis de taxa e confirmação por continuidade dos pacotes.
- Replay da prova 3 reproduz o erro e classifica a última leitura como provisória. A janela física segue sem aprovação. O render e a simulação permanecem sob a política estrita.

### 2026-11-18 — terceira prova física F2-I6B1-PROOF-3

- A prova silenciosa instrumentada descartou o pacote inicial e aceitou 111 pacotes. Os dois históricos ficaram disponíveis após a liberação dos streams.
- O validador interrompeu a prova aos 1.110,3045 ms por `AdvanceOutsideTolerance` no clock de captura. O cálculo foi reproduzido com as leituras brutas. A prova continua sem aprovação. Nenhuma voz foi armazenada.

### 2026-11-18 — F2-I6B1-CLOCK-DIAG

- Diagnóstico numérico por leitura e por stream com motivo específico, âncora, deltas e valores da decisão. A evidência da rejeição e da última leitura aceita é preservada junto do histórico limitado.
- Mantidos os critérios de clock e de encerramento. A prova 2 continua sem causa identificada e a futura prova instrumentada aguarda revisão e autorização.

### 2026-11-18 — segunda prova física F2-I6B1-PROOF-2

- O primeiro pacote descontínuo foi descartado por inteiro. O transporte aceitou 41 pacotes seguintes e produziu e leu frames na FIFO com saída SILENT.
- A prova parou com `InvalidPosition` no clock da captura após 412,5004 ms e ficou sem aprovação. Ambos os streams confirmaram Stop e liberação. Nenhuma voz foi armazenada.

### 2026-11-18 — F2-I6B1-FIX, primeira descontinuidade WASAPI

- Descarte de um único pacote inicial descontínuo válido dentro de 500 ms, com liberação integral no owner e contagem separada. Pacotes descontínuos posteriores ou inválidos continuam encerrando a operação.
- A prova silenciosa agora exige captura aceita, produção e leitura na FIFO e contabilidade final coerente além dos clocks, da janela completa e do encerramento confirmado. Testado com fakes. Nova prova física pendente de revisão e autorização.

### 2026-11-18 — tentativa física F2-I6B1-PROOF

- Executada uma tentativa autorizada com render exclusivamente SILENT. O primeiro pacote de captura veio com DATA_DISCONTINUITY e encerrou a observação em 11,0401 ms. Ambos os streams pararam e foram liberados. A janela de cinco segundos e o movimento dos clocks ficaram sem confirmação.
- Registradas as evidências numéricas sem áudio armazenado. F2-I6B2 aguarda revisão da prova.

### 2026-11-18 — preparação de F2-I6B1-PROOF

- Sincronizada a tarefa de prova nativa. O runner identificou o único teste explícito previsto. Execução física continua condicionada à confirmação do par de dispositivos e à validação física pertinente após a formatação.
- Inventariados backup, clone legado, ZIP e originais da marca para manter uma única pasta de desenvolvimento ativa. As cópias históricas foram preservadas no local.

### 2026-11-18 — recuperação e F2-I6B1, transporte WASAPI silencioso

- Restaurado o ICO original versionado e integrado o commit documental remoto. Preservadas as alterações locais e o backup da recuperação.
- Transporte WASAPI compartilhado com uma thread MTA por stream, priming e saída sempre SILENT, acesso a packets com posse delimitada e reuso de PCM/FIR/SPSC. Clocks da própria sessão e contadores separados por stream.
- O diagnóstico curto conserva o guard quando a liberação do dispositivo falha após completion. O transporte mantém o guard em timeout ou liberação incerta.
- Teste de hardware `Explicit` preparado e pendente. A evidência deste incremento vem de compilação e testes automatizados sem captura física ou reprodução contínua.

### 2026-11-14 — F2-I6A, clock e preparação WASAPI sem áudio

- Ponte COM mínima auditada com PreserveSig, posição/frequência/QPC inteiros e HRESULTs originais; S_FALSE incerto, integrado ao adaptador de relógio existente sem estimador novo.
- Preparação compartilhada com formato exato e clock da mesma sessão, consultas de capacidade/latência e descarte inverso MTA, sem Start ou buffers de áudio. Guard compartilhado com diagnóstico, cancelamento e falha parcial com recursos/lease preservados quando liberação não é confirmada.
- Prova nativa autorizada em captura mono/saída LR48kHz, sem áudio ou alteração de níveis/padrões. Teste opt-in separado da regressão. DSP/EQ/perfis, UI e dependências preservados; sem republicar prévia ou iniciar transporte ativo.
- Documentados ABI/ownership, evidências, limitações e desenho mínimo da futura F2-I6B. Resultados no PROJECT_STATUS; fases não declaradas aceitas.

### 2026-11-14 — EQ-PROFILE-1, ajustes e perfis locais

- Persistência dos dez ganhos, entrada/preamp, ativação e perfil selecionado; edição atual independente do perfil salvo. Criação, salvamento explícito, renomeação e exclusão confirmada, com nomes simples/únicos e até 20 perfis.
- Migração explícita JSON v1→v2 em memória, sem escrita na abertura, preservando campos existentes, backups, conflitos e arquivos protegidos. Saves consolidados e flush no fechamento com mensagem de falha.
- Carregamento antes da interação e controles bloqueados durante atividade. Clipe não persistido, nível digital inicial de 20%, nenhuma captura/reprodução automática ou alteração do Windows. DSP, headroom, backend e dependências preservados.
- Testes de modelo, migração, armazenamento, falha de escrita, duas aberturas, congelamento e fechamento; resultados e republicação no PROJECT_STATUS. A14 manual e Windows 11 pendentes; sem aceitação automática da Alpha/Fase 2.

### 2026-11-14 — CHECKUP-1, cleanup e encerramento seguro

- Corrigida liberação ignorada quando o diagnóstico conclui com erro e descarte pendente; erros primários/cleanup separados, sem liberar callbacks ativos ou repetir Dispose cegamente. Provider/modo/contadores são limpos em cancelamento pré-Start.
- Época de entrega e tratamento de falhas de controle/Tick no worker, Dispose com espera limitada e recursos preservados quando o término não é confirmado.
- Fechamento da janela captura exceções, inicia cleanup independente e permite retry de componentes falhados, sem duplicar tarefas em voo nem fechar à força.
- Transporte exclusivamente simulado trata snapshot indisponível explicitamente; teste de gate/priming reforça o limite do fake. EQ, FIR, headroom, dependências e recursos existentes preservados. Resultados e limitações no status/check-up; F2-I6A/B e validação física pendentes.

### 2026-11-14 — EQ-PREVIEW-1, primeiro EQ audível de dez bandas

- Prévia de EQ real na reprodução explícita da gravação curta existente: 31 Hz a 16 kHz, −15/+15 dB, preamp, headroom conservador, bypass e reset; NAudio.Core reutilizado, sem dependências novas.
- Comparação original/processado com a mesma memória imutável, PCM16/float32 em 44,1/48 kHz, estados independentes por play, proteção final contada e ajustes congelados durante atividade. Taxas não suportadas mantêm o original, com EQ desabilitado.
- Testes de resposta, segurança, provider/cancelamento e UI; fixture antigo aguarda reconciliação completa dos controles. Detalhes/resultados no status e docs/EQ_PREVIEW.md. Sem áudio físico automático, EQ global, novo pipeline de captura ou expansão de FIR/clocks/watchdog.
- Republicação local Release x64 autorizada; teste audível/visual do proprietário pendente. Alpha/Fase 2 não aceitas e F2-I6 continua pendente para a próxima decisão.

### 2026-11-14 — F2-I5, backend WASAPI isolado

- Descritores completos de formato/endpoint/geração e consulta real de mix/capacidade/períodos em MTA, sem Initialize/Start ou escrita de níveis. Mono/LR PCM16/float32 coerentes; formato ambíguo/PCM32/multicanal rejeitado, sem fallback ou conversão oculta.
- Auditoria de APIs públicas NAudio 3.1.0 e Microsoft; lacuna de S_FALSE no wrapper de clock registrada como bloqueio do caminho nativo ativo. Buffer/latência efetivos não consultáveis sem inicializar permanecem desconhecidos.
- Adaptação de relógio por deltas inteiros/UInt128, qualidade, flags, frescor, identidade e overflow. Sessão exclusivamente simulada com worker MTA, produtor/consumidor separados, FIR/SPSC reais, priming, contadores/média temporal por eventos fechados, gate de entrega e descarte após quiescência.
- Testes de PCM, FIR/FIFO por hash, mute/nível digital, clocks acima de 2⁵³, falhas, interlock, filas limitadas, start parcial, stop/cancel em voo, callbacks antigos e timeout/descarte seguro. Sem mudanças no Desktop, efeitos, drivers ou dependências. Resultados finais no status/plano de testes.
- Republicação local autorizada da prévia Release x64 com hotfix de área útil já existente, recursos/licenças e armazenamento de teste isolado. A01–A12/Windows 11 e causa histórica completa do runner seguem pendentes. Nenhuma integração física, release pública, F2-I6 ou Fase 3.

### 2026-11-14 — F2-I4, resampling adaptativo de amostras e integração sintética

- AdaptiveResampler interno: FIR interpolado de 257 fases/161 taps, cursor inteiro/Q48, 48→48 e 44,1↔48 mono/LR, ±500 ppm, slew 25 ppm/s, geração, parada e EOF por trajetória. Nominal/identidade/96→48 preservados.
- Mapeamento somente do prefixo aceito e proteção por estágio sem recontar sufixo; zero alocação recorrente medida. Qualidade estática/transições atende metas prévias; prova liga controlador, adaptador/FIR e SPSC com amostras geradas/clocks independentes, perdas/priming/descarte explícitos.
- Corrigida rejeição cumulativa do estimador para não confirmar baseline inválida, invalidar Latest e exigir recuperação. Watchdog valida identidade própria e limita coleta de pipes; probes de timeout/pipe exercitados com recusas seguras de conhost sem nonce.
- Corrigida falha comprovada de inicialização com área útil de monitor inválida, preservando posição salva quando indisponível. Fixture de preferências convertido a loop STA assíncrono após timeout observado depois de Disposed; causa histórica completa permanece pendente.
- Restore locked auditado, builds Debug/Release x64 sem erros/avisos; suítes finais com 375 aprovados por configuração (336 anteriores + 39 novos), zero falhas/ignorados. Falhas intermediárias e resultados completos documentados no status/protocolo.
- Sem backend/streams físicos novos, UI de DSP, volumes/padrões alterados, driver, release pública ou redesign. Marca e pacote publicado preservados; A01–A12/Windows 11 pendentes. F2-I5 planejada, não iniciada.

### 2026-11-14 — F2-I3, relógios independentes e estabilidade de testes

- Tipos internos de relógio/observação, estimador por deltas em referência comum e controlador de razão saída/entrada com feed-forward, PI, anti-windup, ±500 ppm e slew limitado. IDs/gerações, unidades, tempo/posição inválidos, frescor e descontinuidade verificados.
- Simulação determinística realmente executada de eventos independentes: 18 pares ligados/desligados de 900 s, 48→48, 44,1↔48 e ±50/100/300 ppm, jitter, offsets, contabilidade Q32 e fila limitada. Zero perdas com controle nos cenários regulares; convergência 21,8–76,7 s. Saturação, perdas, priming e recuperação explícitos nas falhas, sem transformar amostras ou abrir hardware.
- Corrigida observação instantânea de ocupação no modelo após falha da meta de convergência: média temporal remove alias de blocos/cadência, preservando critérios e ganhos. Ensaios de consumo parcial nominal quantificam remapeamento redundante, sem alterar o FIR aprovado.
- Coletor de avisos com asserções, fases de ciclo de vida e deadline fora da STA em testes de preferências; watchdog externo restrito ao runner próprio. Bloqueio anterior não reproduzido nas tentativas limitadas; causa permanece hipótese, sem mudança de UI/armazenamento em produção.
- Restore locked auditado, builds Debug/Release x64 sem erros/avisos, 336 testes aprovados por configuração (313 anteriores + 23 novos). Repetições e resultados completos no status/protocolo; zero alocação na thread medida dos novos ensaios estáveis.
- Alpha, marca, executável publicado, dependências e áudio existentes preservados. Integração física continua bloqueada por revisão/A01–A12 e Windows 11. Planejado resampler adaptativo real antes de backend; sem F2-I4, EQ, redesign, driver ou release pública nesta etapa.

### 2026-11-13 — F2-I2, adaptação de canais e resampling nominal

- Políticas explícitas de preservação, duplicação mono→L/R e média 0,5L+0,5R; layouts desconhecidos, multicanal, buffers inválidos e sobreposição recusados. Não finitos sanitizados antes do histórico; proteção final independente.
- FIR causal streaming de 161 taps com fases racionais inteiras, memória pré-alocada, identidade exata sem atraso, 44,1↔48 e 96→48 kHz, consumo/produção parcial, reset e EOF/flush com contagem explícita da cauda.
- Trajetórias sintéticas com adaptação/resampling, fila SPSC original e proteção realmente exercitadas, sem integrar à UI nem alterar o teste de microfone. Ciclo de vida da base testado em concorrência e contrato não transacional de operações em voo documentado.
- Metas fixadas antes da medição atendidas na grade: erro útil máximo 0,00148773 dB; pior alias -81,11294 dB. Release final: cerca de 2,05 milhões de frames de saída/s e zero bytes alocados na thread medida; método, ripple, imagens, memória e limitações em docs/RESAMPLING_QUALITY.md.
- Restore locked auditado, Debug/Release x64 sem erros/avisos; 313 testes aprovados por configuração (283 anteriores + 30 novos), sem falhas/ignorados. Plano de backend/relógios/drift futuro atualizado, sem alegar sincronização física.
- Duas outras tentativas de regressão ficaram incompletas; log identificou teste de preferências WinForms já existente em andamento após 304 aprovados. Classe passou isolada e suítes finais passaram; causa da intermitência permanece pendente, sem alegar correção. Runners próprios encerrados, diagnósticos locais ignorados e limite do timeout registrado.
- Alpha, marca, recursos e executável publicado preservados; A01–A12 e Windows 11 continuam pendentes conforme evidências. Sem novo áudio físico, driver, redesign, release pública, efeitos ou F2-I3.

### 2026-11-13 — F2-I1, fundação isolada do motor

- Pipeline sintético realmente executado nos testes: PCM16/float32, formato intercalado, fila SPSC pré-alocada, neutralidade e proteção de NaN/Infinity/clipping.
- Overflow com descarte de frames mais novos, underrun com silêncio, snapshots, wrap/rollover e gates de geração/cancelamento/parada/reset/descarte em quiescência.
- Plano AUDIO_ENGINE_PLAN com ownership/ordenação de memória, limites, auditoria NAudio 3.1.0 e contratos futuros de canais/resampling/drift/backend contínuo.
- Restore locked auditado, Debug/Release sem erros/avisos e 283 testes aprovados por configuração (252 anteriores + 31 novos). Stress concorrente e medição de 51,2 milhões de frames estéreo, zero alocação recorrente na thread medida; métricas completas no plano/status.
- Nenhuma integração com UI ou áudio físico; comportamento, marca e prévia Alpha preservados. Fase 1 permanece em validação física. Funcionalidade antes do redesign; sem driver, EQ, monitoramento automático ou release pública.

### 2026-11-13 — identidade oficial do Wave Studio

- Original da logo fornecida pelo proprietário preservado em assets/branding, com transparência e SHA-256 registrado. ICO oficial com sete resoluções e PNGs derivados reproduzíveis.
- Logo no cabeçalho e README; ícone nativo do EXE e da janela para taskbar/Alt+Tab/atalhos futuros. Recursos incorporados, sem dependência de arquivos externos de imagem.
- Metadados oficiais de produto, descrição, projeto e versão Alpha, guia de identidade e preparação para distribuição futura, sem release pública ou instalador.
- Restore locked, builds Debug/Release sem erros/avisos e 252 testes aprovados por configuração. Executável x64 republicado e iniciado/fechado normalmente. Áudio e F1-I6-METER preservados; conferência visual do shell/DPI e aceitação física continuam pendentes.

### 2026-11-13 — F1-I6-METER, pico da entrada na aba Testar microfone

- Barra horizontal 0–100%, texto numérico, disponibilidade e explicação de amplitude na aba Testar microfone, preservando a barra de Dispositivos e o volume digital de reprodução separado.
- Reutilizado AudioWatchWorker a cada 100 ms, sem segundo worker de picos ou captura automática. Medição ativa nas duas abas de áudio e pausada nas demais/minimizada.
- Proteção por seleção/revisão, idade da leitura e invalidação em troca, recuperação e encerramento. Pausa/retomada descarta leitura pendente; zero, indisponibilidade e atraso são distintos.
- Corrigida ativação inicial no Shown. Restore locked com auditoria, builds Debug/Release sem erros/avisos e 248 testes aprovados em cada configuração (228 anteriores + 20 novos).
- Novo executável Release x64 na pasta habitual, com dependências/licenças e roteiro A12. Complemento remoto ed19c6e incorporado: sucesso relatado da gravação/reprodução após ajustes próprios não especificados, preservando histórico do zumbido e ausência de causa confirmada. Subcasos A11, A12 e Windows 11 pendentes. Alpha continua em validação, sem iniciar Fase 2.

### 2026-11-12 — F1-I6-MIC, teste local de microfone

- Aba Testar microfone com gravação explícita de até 10 s, parada, reprodução explícita na saída selecionada, parada da reprodução e descarte. Sem monitoramento contínuo.
- Trecho em memória limitado a 8 MiB e 10 s, sem arquivo/envio/persistência. Descarte na mudança de alvos/formato, atualização, desconexão, suspensão e fechamento.
- Nível digital de reprodução inicialmente em 20%, com ajuste 0–100% e sem boost. Preservados volumes/mute/padrões do Windows e balanço indisponível em mono.
- Worker MTA separado da UI, snapshots, cancelamento/geração, callbacks limitados e descarte de streams/dispositivos. APIs públicas WasapiRecorderBuilder/WasapiPlayerBuilder da versão 3.1.0, sem novas dependências.
- Restore locked com auditoria, builds Debug/Release sem erros/avisos e 228 testes aprovados em cada configuração (184 anteriores + 44 novos). Novo executável conferido em abertura/fechamento com configuração isolada, sem gravação real.
- Roteiro A11 e documentação atualizados. Volume/mute de captura confirmados pelo proprietário são evidências parciais; A01–A11 e Windows 11 continuam aguardando aceitação física completa.
- Após confirmação de fechamento normal da prévia anterior, pacote F1-I6-MIC promovido para artifacts/alpha-preview/win-x64 com conferência dos 14 arquivos por SHA-256. Caminhos de execução atualizados; código e dependências preservados.

### 2026-11-12 — F1-I6, checkpoint técnico de validação e pacote x64

- Sincronizado até 8d0e688 e tratados os pontos 1–3 da revisão F1-I5. Arquivo excessivo recebe estado protegido, com escrita/reset/recuperação bloqueados e preservação em crescimento concorrente.
- Falhas previsíveis de validação/gravação são informadas e observadas no fechamento. Seleções ficam desabilitadas até a carga. Valores indisponíveis e balanço em silêncio são tratados ao salvar.
- Restauração confirma canais finais após unmute, com tolerância de 2% do nível-base, informa balanço não observável em zero e avisa sobre áudio audível ao confirmar unmute.
- Pico real por sessão no mixer a cada segundo, com identidade por instância, descarte dos wrappers, pausa durante edição e indicação de atraso/indisponibilidade.
- Restore locked, builds Debug/Release com zero erros/avisos e 184 testes aprovados em cada configuração, incluindo os 174 anteriores.
- Publicação local framework-dependent AMD64, avisos de licença, SHA-256 e roteiro ALPHA_ACCEPTANCE. Processo ensaiado por 600,34 s e encerrado normalmente, com dados de CPU/RAM/handles registrados.
- Automação visual indisponível por falha do pipe nativo. Validação física e Windows 11 aguardam evidências do proprietário. A Alpha permanece com validação pendente.
- Após o checkpoint 06d2c3f, proprietário confirmou que testará a prévia pronta e que dispõe somente de Windows 10. Roteiro e status atualizados para iniciar A01/A02, com Windows 11 pendente por ambiente indisponível.

### 2026-11-12 — F1-I5, preferências versionadas e recuperação segura

- Sincronização até 4e2d690 e execução da tarefa autorizada, preservando NEXT_TASK.md, REVIEW.md e requisitos.
- Configuração JSON por usuário com schemaVersion 1, seleções por direção, modo automático/ID explícito ausente, janela validada e preferências opcionais de volume/mute/balanço por endpoint.
- Salvar valores atuais, prévia e confirmação para restaurar com alvo/geração/suporte/níveis revalidados, consulta após aplicação e indicação de falha parcial.
- Escrita fora da UI com exclusão, hash de conflito, temporário na mesma pasta, flush, substituição e backup. Corrupção e versão incompatível preservadas, recuperação/reset explícitos e originais inválidos arquivados.
- Falha terminal e leituras antigas do worker agora exibem indisponibilidade. Retomada/manual e eventos de ciclo de vida invalidam gerações mesmo com ID igual. Metadados preservam controles. Seleção automática favorece padrão ativo e informa fallback.
- Mixer retém pedidos de reconciliação durante uma leitura. Preferências de sessão seguem indisponíveis até comprovação de identidade durável. Medidores contínuos por sessão seguem pendentes da avaliação de ciclo de vida/custo.
- Build com zero erros/avisos e 174 testes aprovados, incluindo os 101 anteriores. Integrações isoladas de reinício e preservação de arquivos aprovadas. Restauração física, áudio ativo, hotplug, retomada, layout/DPI e Windows 11 continuam pendentes.

### 2026-11-12 — F1-I4, picos reais e recuperação de dispositivos

- Repositório sincronizado até b4a6223 e executada a tarefa autorizada de NEXT_TASK.md, preservando os documentos normativos e a revisão.
- Barras reais de pico de reprodução/captura, percentual e dBFS, silêncio e indisponibilidade distintos, consulta aproximada de 10 Hz com pausa quando ocultas.
- Notificações públicas NAudio de adição, remoção, estado, padrões e propriedades relevantes, coalescência de rajadas, fallback de cinco segundos e recuperação na retomada.
- Worker MTA persistente proprietário de inscrições e medidores, snapshots por geração e descarte ordenado. Seleção explícita preservada e modo seguir padrão independente por direção.
- Mixer reconciliado por chave com preservação de foco/edição, eventos vinculados à geração e acesso isolado a sessões inacessíveis durante busca do alvo.
- Restore locked e build com zero erros/avisos. 101 testes aprovados, incluindo os 77 anteriores e seis integrações de leitura. Picos observados em zero nos endpoints e em cinco sessões. Executável iniciado e controles reconhecidos por acessibilidade.
- Medidor contínuo individual por sessão adiado para avaliação de ciclo de vida/custo. Resposta à fala/música, USB, padrões externos, suspensão, escritas, layout e Windows 11 continuam pendentes de ensaio físico.

### 2026-11-12 — F1-I3, mixer de aplicativos

- Sincronizadas NEXT_TASK.md e REVIEW.md até 4ab114d e executada F1-I3 mediante autorização do proprietário.
- Mixer de sessões reais por endpoint/instância, nome/processo quando disponível, PID auxiliar, estado, sons do sistema, volume e mute individuais.
- Reconciliação por polling, snapshots sem COM, comandos serializados e invalidação de seleção/instância antigas.
- Corrigida retenção de gerações do Incremento 2, com liberação após drenagem, confirmação dos sliders ao fim do gesto, rolagem da janela e proteção da restauração de balanço contra mudança externa detectável.
- Build com zero erros/avisos e 77 testes aprovados, incluindo os 56 anteriores. Integração de leitura encontrou quatro sessões reais e mediu custo de consultas. Escritas físicas e layout permanecem pendentes.
- Separados estado atual e histórico em PROJECT_STATUS.md. Incremento 4 aguarda orientação.

### 2026-11-12 — Fase 1, Incremento 2

- Publicado o Incremento 1 no commit 60238ea com a mensagem autorizada.
- Controles de volume 0–100%, mute independente, balanço estéreo condicional e centralização para os endpoints selecionados.
- Sincronização externa por polling em segundo plano, comandos associados a ID/revisão da seleção e cancelamento de comandos atrasados.
- Confirmação de suporte por leitura/releitura, mensagens de indisponibilidade, descarte de COM/callbacks e encerramento assíncrono das filas.
- Balanço com nível-base preservado, atenuação, validação de canais L/R e tentativa de restauração após atualização parcial.
- Restore/build aprovados, build final com zero erros/avisos e 56 testes aprovados. Leituras de hardware e controles da janela conferidos por acessibilidade. Escritas físicas e revisão visual pendentes.

O Incremento 2 recebeu aprovação provisória para publicação e revisão técnica, com commit/push autorizados. O Incremento 3 aguarda a revisão técnica e a configuração do fluxo de comunicação ChatGPT/Codex pelo GitHub.

### 2026-11-12 — Fase 1, Incremento 1

- Solução .slnx, SDK .NET 10 fixado, projetos Desktop/Audio/Tests x64 e referências.
- Dependência específica NAudio.Wasapi 3.1.0, xUnit v3 4.0.3 com MTP 2.5.2 e lock files dos três projetos.
- Janela pt-BR com seleção independente, padrões de multimídia/comunicações, estados, IDs e formato real do mix.
- Consultas serializadas em worker MTA, snapshots independentes de COM, descarte local, cancelamento e mensagens de indisponibilidade.
- README corrigido e instruções de execução/testes atualizadas.
- Build com zero erros/avisos, 16 testes aprovados e enumeração real de 67 endpoints. Executável iniciado e conteúdo da janela lido por acessibilidade. Avaliação visual/interativa e testes físicos adicionais pendentes.

Windows 10 permanece experimental. O Incremento 2 foi autorizado após a publicação desta entrega.

### 2026-11-12 — Fase 0

- Leitura das instruções e requisitos, inspeção de Git, SDK, sistema e estrutura.
- Documentação de arquitetura, suporte/limites, dependências e caminho preferido de integração global por endpoints virtuais.
- Plano incremental da v0.1.0 Alpha e critérios automatizados, físicos e de integração externa.
- Registro do estado real do projeto e navegação de documentação no README.

A v0.1.0 Alpha permanece planejada, com código, build e testes de produto pendentes.
