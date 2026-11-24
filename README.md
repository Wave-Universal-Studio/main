<div align="center">
  <img src="assets/branding/icons/WaveStudio-256.png" width="128" height="128" alt="Logotipo do Wave Studio" />
  <h1>Wave Studio</h1>
  <p><strong>Seu áudio. Seu microfone. Seu estúdio.</strong></p>
  <p>Central universal de áudio para Windows, independente da marca do equipamento.<br>Mixagem geral de aplicativos, música, comunicação e streaming, com ajustes específicos também para jogos.</p>
  <p><code>v0.1.0 Alpha</code> · <code>Windows x64</code> · <code>C# / .NET 10</code> · <code>PT-BR · EN · ES</code></p>
  <p><a href="#recursos">Recursos</a> · <a href="#estado-do-projeto">Estado</a> · <a href="#roadmap">Roadmap</a> · <a href="#executar">Executar</a> · <a href="#documentação">Documentação</a></p>
</div>

---

### Sobre o aplicativo

O **Wave Studio** é uma central universal para controlar e personalizar o áudio de headsets, microfones e aplicativos no Windows.  
Sua mesa digital foi planejada para **mixagem geral de áudio**, com controle por aplicativo, comunicação, música, streaming e recursos específicos para jogos.  
O projeto está em fase **Alpha**: parte dos controles já funciona, enquanto os barramentos independentes e os efeitos avançados continuam em desenvolvimento.

> [!IMPORTANT]
> **Alpha em desenvolvimento.** O aplicativo ainda não tem instalador ou release pública estável. Equalização global, áudio virtual contínuo e barramentos ligados a saídas reais **não** estão disponíveis como funções prontas.

## Recursos

| Módulo | Disponível na versão Alpha |
| --- | --- |
| **Dispositivos** | Enumeração de entradas/saídas, seleção, volume, mute, balanço estéreo e detecção de alterações. |
| **Mesa de som** | Faders de 0–100%, mute e pico das sessões reais de aplicativos; master da saída do Windows; grupos e filtros temporários. |
| **Jogo / Comunicação** | Balanço que atenua o grupo oposto sem elevar o volume original, com restauração condicional e avisos de conflitos. |
| **Microfone** | Teste de gravação curta (até 10 s) e reprodução explícita, com indicação de captação. |
| **Equalizador** | Prévia de EQ de 10 bandas, preamp, bypass, comparação A/B e perfis locais. |
| **Roteamento externo** | Diagnóstico **somente leitura** de dispositivos, formatos, pareamento e compatibilidade; guia de configuração e restauração manual. |
| **Configurações** | Preferências persistentes, backup e restauração; interface em português brasileiro, inglês e espanhol, com opção Seguir Windows. |

O equalizador atual processa **apenas a gravação de teste reproduzida pelo Wave Studio**; não altera globalmente jogos, Discord ou OBS. Os picos são leituras instantâneas e dependem dos recursos fornecidos pelo Windows.

### Mesa de som e idioma

Em **Dispositivos**, selecione a saída. Na **Mesa de som**, ajuste cada aplicativo por fader ou mute; a grade técnica continua em **Detalhes das sessões**. Para usar **Balanço Jogo / Comunicação**, atribua sessões ativas aos dois grupos. Centralizar ou sair da mesa tenta restaurar os níveis modificados, mas alterações externas e sessões encerradas podem impedir a restauração completa.

**Atenção:** o balanço e o master atuam sobre volumes **reais do Windows**, podendo afetar chamadas e transmissões. Não são barramentos pessoais independentes. A opção **Configurações → Idioma** aplica a tradução selecionada na próxima abertura do aplicativo.

## Estado do projeto

**24/11/2026 — F5-I1 até F5-I2C2 integradas; ligação física contínua ainda em desenvolvimento.**

- **Base da `main`:** mesa de som, balanço Jogo/Comunicação e três idiomas implementados. A última regressão consolidada da F4-I2 registrou **914 testes aprovados em Debug e 914 em Release**, com sete testes físicos `Explicit` excluídos.
- **[F5-I1 — integrada](https://github.com/Wave-Universal-Studio/main/commit/787df9181f62bc5b340300ee53d1b0ecabf83eca):** núcleo PCM de **Personal Mix** e **Stream Mix**, com matriz de envios, ganho/mute por barramento e isolamento; processamento testado com transporte injetado. **Não** equivale a áudio contínuo independente para OBS/Discord.
- **Validação:** [GitHub Actions Windows x64 F5-I2C2](https://github.com/Wave-Universal-Studio/main/actions/runs/45340451874): **959 testes aprovados em Debug e 959 em Release**, zero falhas, sete Explicit excluídos por configuração e builds limpos. O scheduler permanece opt-in e ainda não inicia streams contínuos reais em hardware.
- **Pendências:** estabilidade e recuperação prolongadas em hardware, latência/drift, aceitação instrumental de rotas externas e conexão dos barramentos com entradas/saídas contínuas.

O proprietário relatou funcionamento manual dos dois fluxos externos estudados; isso não substitui medições instrumentadas. Para o histórico e as evidências, consulte [Status](PROJECT_STATUS.md), [Plano de testes](docs/TEST_PLAN.md) e [Próxima tarefa](NEXT_TASK.md).

## Roadmap

| Fases | Objetivo |
| --- | --- |
| **1** | Controles reais de dispositivos e sessões do Windows — base implementada. |
| **2–3** | DSP, WASAPI, sincronização e integração externa — fundação/diagnóstico experimental. |
| **4–5** | Mesa, Jogo/Comunicação, barramentos e matriz — mesa implementada; núcleo de barramentos em revisão. |
| **6–7** | EQ global/avançado, dinâmica, supressão de ruído e tratamento de microfone. |
| **8–10** | Soundboard, efeitos/transformação de voz e surround virtual. |
| **11–12** | Gravação multifaixa, perfis, automação, otimização e distribuição. |

Também estão planejados um hub visual de equipamentos, diagnósticos/reparos seguros, aparência personalizável e estudos de integração com VST3, Equalizer APO e AutoEQ. Veja o [plano mestre](docs/PRODUCT_MASTER_PLAN.md) para o escopo completo.

## Compatibilidade

**Windows 11 x64** é o alvo prioritário, ainda com validação física completa pendente. **Windows 10 x64** é experimental. USB, Bluetooth, P2/P3, HDMI/DP e interfaces são tratados conforme os endpoints e recursos realmente expostos pelo Windows.

A aplicação usa processamento local e não envia gravações a serviços externos. O motor é desenvolvido em C#/.NET 10 com WinForms, NAudio, Core Audio/WASAPI e testes automatizados. O ambiente Codex Cloud/Linux pode compilar alvos Windows, **mas não substitui a execução WinForms e os testes de integração em Windows**.

## Executar

Requer **Windows x64** com o SDK indicado em [global.json](global.json). Na raiz do repositório:

```powershell
dotnet restore WaveStudio.slnx --locked-mode
dotnet build WaveStudio.slnx -c Debug --no-restore
dotnet run --project src/WaveStudio.Desktop -c Debug
```

Para validar, consulte o [plano de testes](docs/TEST_PLAN.md): a suíte Windows é executada separadamente, e os testes `Explicit` não devem ser disparados sem consentimento. Não há instalador público nesta fase.

## Documentação

| Para conhecer | Para desenvolver e revisar |
| --- | --- |
| [Requisitos](docs/REQUIREMENTS.md) · [Plano mestre](docs/PRODUCT_MASTER_PLAN.md) · [Hub](docs/PRODUCT_HUB_PLAN.md) | [Arquitetura](docs/ARCHITECTURE.md) · [Motor de áudio](docs/AUDIO_ENGINE_PLAN.md) · [WASAPI](docs/WASAPI_BACKEND_PLAN.md) |
| [Progresso](PROJECT_STATUS.md) · [Histórico](CHANGELOG.md) · [Roadmap imediato](NEXT_TASK.md) | [Testes](docs/TEST_PLAN.md) · [Revisões](REVIEW.md) · [Instruções para agentes](AGENTS.md) |
| [Identidade visual](assets/branding/README.md) · [Licenças](docs/THIRD_PARTY_NOTICES.md) | [Guia de continuidade](CHAT_CONTINUITY.md) · [Prova externa](docs/EXTERNAL_ROUTE_PROOF.md) |

> O repositório é uma **Alpha técnica**: o README destaca recursos verificáveis, enquanto `docs/` preserva decisões, limitações e evidências completas.

---

<div align="center">
  <img src="assets/branding/icons/WaveStudio-64.png" width="40" height="40" alt="Wave Studio" />
  <p><strong>Wave Studio</strong> · Controle real. Áudio personalizado.</p>
</div>
