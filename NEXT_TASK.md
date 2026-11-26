# Wave Studio — próxima entrega

**Checkpoint de 26/11/2026.** A F5-I2C3 abaixo foi integrada em `4f7e4be` pela [PR #18](https://github.com/Wave-Universal-Studio/main/pull/18). A nova referência é de **979 testes comuns por configuração**, com sete provas Explicit excluídas. [CI Windows x64](https://github.com/Wave-Universal-Studio/main/actions/runs/45896014713).

O próximo ponto de integração é coletar as leituras dos clocks nos owners WASAPI já preparados e ligá-las ao corte QPC do scheduler. A fronteira atual ainda exige confirmação nativa do ciclo de vida e da recuperação. As provas físicas em aberto seguem no [relatório de aceitação](docs/PHYSICAL_VALIDATION.md).

## Escopo entregue na F5-I2C3

**F5-I2C3-CLOCK-ADMISSION-AND-RECOVERY — admissão temporal e recuperação dos owners de áudio.**

**Base aprovada:** F5-I2C2 integrada via PR #14 no commit `3b2c0a55e1026942868c5b659a67b8dc9369109c`. [CI Windows x64](https://github.com/Wave-Universal-Studio/main/actions/runs/45340451874): **959 testes comuns Debug/959 Release**, zero falhas, 7 Explicit excluídos por configuração, builds sem avisos. Todos os incrementos F5-I1 até F5-I2C2 estão na main. Esforço EXTRA ALTO. Autorização para código/PR/merge rotineiros com segurança e CI verde, sem despesas ou alterações físicas do Windows.

## Objetivo real e fronteira

F5-I2C2 permite que um owner MTA previamente preparado capture um bloco de 10 ms, faça mixagem PCM em Personal/Stream e entregue a owners de render explicitamente associados; ainda depende de um `now100ns` injetado e fontes já abertas. O próximo incremento deve construir uma **camada de admissão de relógios verificáveis e recuperação** baseada nos contratos já presentes `NativeClockSample`, `WasapiClockAdapter`, `ClockRatioController`, `WasapiCaptureTiming`, `TransportActivityGuard` e nos geradores/tickets. Antes de admitir cada bloco, distinguir relógio desconhecido, timestamps stale/futuros, regressão de posição, diferenças de frequência/canais/gerações e lacunas; impedir a entrega até existir evidência nominal suficiente. Não presumir relógio comum entre duas saídas físicas. Falha deve encerrar/quarentenar ou exigir rearmamento explícito, nunca continuar entregando áudio de geração anterior.

Consultar código de produção e AGENTS.md, REVIEW.md, CHAT_CONTINUITY.md, PROJECT_STATUS.md, docs/REQUIREMENTS.md, docs/AUDIO_ENGINE_PLAN.md, docs/WASAPI_BACKEND_PLAN.md, docs/TEST_PLAN.md. Não reimplementar dispositivos/Core Audio do zero. Preferir incrementos pequenos com integração real ao OwnedBusScheduler, não apenas DTOs e um mockup. Se não houver forma de medir QPC sem abrir hardware, usar relógios injetados e explicitar a fronteira. Preservar guard do owner e Stop cooperativo. Sem alocação recorrente/callback nem loops/feedback. Destino Stream externo começa inativo, mic/chat/agregado só com autorização explícita e proteção mix-minus.

## Validação e produto

Novos testes Windows usando relógios nativos falsos e owners MTA simulados para QPC válido, sem tempo, futuro, fora da janela, posição regressiva, offset/discrepância entre fontes, mudança de geração, perda de sinal, cancelamento, Stop, falha ReleaseBuffer, backpressure, 1→2 buses e privacidade. Nenhum teste `Explicit`, instalação de driver, reboot, mudança de padrão Windows, operação física de VB-CABLE ou acionamento de OBS. GitHub Actions Windows x64 obrigatório: restore locked auditado, builds Debug/Release zero avisos e regressões **baseline 959 + novos casos** por configuração. PR pequena com CI verde e revisão segura antes de merge. Preservar README público COMPLETO com logo e mini resumo de três linhas; atualizar PROJECT_STATUS, CHANGELOG, arquitetura e testes sem fingir recursos utilizáveis em hardware. Somente então avançar o roadmap.
