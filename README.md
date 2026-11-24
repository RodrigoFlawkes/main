<div align="center">
  <img src="assets/branding/icons/WaveStudio-256.png" width="144" height="144" alt="Logo oficial do Wave Studio" />
  <h1>Wave Studio</h1>
  <p><strong>Seu áudio. Seu microfone. Seu estúdio.</strong></p>
  <p>Central universal de áudio para Windows, independente de fabricante.<br>Controle, personalização e mixagem para jogos, comunicação, streaming e música.</p>
  <p><code>v0.1.0 Alpha</code> · <code>Windows x64</code> · <code>C# / .NET 10</code> · <code>PT-BR</code></p>
  <p><a href="#recursos">Recursos</a> · <a href="#roadmap">Roadmap</a> · <a href="#experimentar">Experimentar</a> · <a href="#documentação">Documentação</a></p>
</div>

---

> [!IMPORTANT]
> **Alpha experimental.** Os controles do Windows e o teste de microfone já funcionam na prévia. Equalização global, monitoramento contínuo, microfone virtual e roteamento externo ainda estão em desenvolvimento. Não há instalador ou release pública.

> [!NOTE]
> **Continuidade entre chats do ChatGPT:** leia [CHAT_CONTINUITY.md](CHAT_CONTINUITY.md) antes de retomar o projeto. O guia reúne o fluxo, as decisões, os checkpoints e o protocolo de recuperação.

## Barramentos internos em revisão

A F5-I1 implementa **Personal Mix** e **Stream Mix** com processamento PCM, envios independentes e medidores calculados sobre as amostras de saída. A integração usa um transporte injetado. Captura contínua de aplicativos e conexão dos barramentos a OBS, Discord ou dispositivos físicos dependem da próxima etapa de integração. A regressão da solução usa GitHub Actions em Windows x64. [Arquitetura do motor](docs/ARCHITECTURE.md#f5-i1-bus-matrix-core) · [Workflow](.github/workflows/windows-x64.yml)

## Recursos

### Disponíveis na prévia

- Seleção de entrada e saída, volume, mute e balanço estéreo quando suportado.
- Mesa de som com faders, mute e picos de sessões reais, grupos e master da saída do Windows.
- Teste de microfone de até 10 segundos, com reprodução explícita.
- EQ de 10 bandas, preamp, bypass, comparação A/B e perfis locais.
- Preferências persistentes, backup, restauração, detecção de dispositivos e idioma da interface.

O EQ atual atua **somente sobre a gravação curta reproduzida dentro do Wave Studio**. Não modifica o áudio de jogos, Discord ou OBS. O áudio do teste permanece temporariamente em memória.

### Mesa de som e idioma

Escolha a saída em **Dispositivos**. Em **Mesa de som**, arraste o fader do aplicativo ou use as setas, alterne o mute e atribua grupos às sessões presentes. Use o seletor acima das faixas para filtrar os grupos. A aba **Detalhes das sessões** mantém a grade com volume numérico, PID e instância. O pico de cada faixa é uma amostra instantânea e pode perder eventos breves. O master mostra o pico agregado e controla o volume e mute dessa saída no Windows.

Para usar **Balanço Jogo / Comunicação**, atribua pelo menos uma sessão ativa a cada grupo. Mova o controle para o lado que deseja ouvir em destaque. O outro lado é atenuado a partir dos volumes originais. **Centralizar** tenta restaurar esses volumes. Ao sair da mesa, trocar de saída ou fechar normalmente, o Wave Studio também tenta restaurar os valores que ainda correspondem aos que aplicou. Se houver conflito ou faltar confirmação, a mesa mostra um aviso e conserva valores alterados externamente.

O balanço altera os volumes de sessão do Windows na saída selecionada e pode afetar o que chega à transmissão. Solo de sessão fica fora desta versão porque exigiria silenciar outros aplicativos, inclusive chamadas, com restauração sujeita a concorrência.

Em **Configurações → Idioma**, escolha **Português (Brasil)**, **English (United States)**, **español (España)** ou **Seguir Windows**. A escolha é salva automaticamente e vale na próxima abertura do Wave Studio. Seguir Windows usa o inglês ou espanhol quando corresponde a uma dessas famílias e pt-BR para idiomas sem tradução disponível. A formatação numérica segue a configuração regional do Windows.

### Visão do produto

| Área | Funcionalidades planejadas |
| --- | --- |
| **Hub** | Equipamentos, catálogo visual e personalização |
| **Áudio** | EQ básico, 31 bandas e paramétrico, dinâmica, efeitos e surround 7.1 |
| **Microfone** | EQ, boost, redução de ruído, tratamento vocal, alteração de voz e Soundboard |
| **Mixer** | Mesa digital, faders, canais, barramentos, game/chat balance e gravação |
| **Reparos** | Diagnóstico e correções seguras de áudio e dispositivos |
| **Config** | Idiomas, temas, cores, wallpaper, animações, perfis e atalhos |

Também estão previstos integração com aplicativos externos, mix-minus e estudos de compatibilidade com VST3, Equalizer APO e AutoEQ. O acesso Apoiar será voluntário e não bloqueará recursos.

[Plano completo do produto](docs/PRODUCT_MASTER_PLAN.md)

## Estado atual

**24/11/2026 · F4-I2 — balanço Jogo / Comunicação e três idiomas**

A mesa usa as sessões de reprodução do dispositivo escolhido. Os dois fluxos externos foram confirmados manualmente pelo proprietário e seguem sem telemetria associada a uma prova instrumentada. Monitoramento contínuo, recuperação em hardware, latência acústica e drift permanecem como pendências técnicas de release.

[Status detalhado](PROJECT_STATUS.md) · [Próxima tarefa](NEXT_TASK.md) · [Roteiro agrupado](docs/EXTERNAL_ROUTE_PROOF.md)

## Roadmap

| Fase | Objetivo |
| --- | --- |
| 0–1 | Arquitetura e controles básicos do Windows — base implementada |
| 2 | Motor de áudio, resampling, sincronização e monitoramento — em desenvolvimento |
| 3 | Integração real com aplicativos externos |
| 4–5 | Mesa digital, barramentos e roteamento |
| 6–7 | Equalização e tratamento de áudio/microfone |
| 8–10 | Soundboard, alteração de voz e surround |
| 11–12 | Gravação, perfis, automação, otimização e distribuição |

## Compatibilidade

**Windows 11 x64** é a plataforma prioritária (validação física pendente); **Windows 10 x64** é experimental. O projeto trabalha com os endpoints expostos pelo Windows: USB, Bluetooth, P2/P3, HDMI/DP, wireless e interfaces de áudio. Recursos de hardware dependem do suporte real de cada dispositivo.

Processamento local, sem envio de áudio pelo aplicativo. Sem driver próprio para os controles básicos.

## Experimentar

Na raiz do repositório, com o SDK definido em [global.json](global.json):

```powershell
dotnet run --project .\src\WaveStudio.Desktop -c Release
```

A prévia local anterior, se gerada, está em `artifacts/alpha-preview/win-x64/WaveStudio.exe` e requer .NET Desktop Runtime 10 x64. Não é um instalador público.

[Guia de teste e aceitação](docs/ALPHA_ACCEPTANCE.md) · [Teste de microfone e EQ](docs/EQ_PREVIEW.md)

## Desenvolvimento

**Stack:** C# · .NET 10 LTS · WinForms · NAudio · Core Audio/WASAPI · JSON · xUnit v3.

```powershell
dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build WaveStudio.slnx -c Debug --no-restore
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
dotnet test --solution WaveStudio.slnx -c Debug --no-build
```

`src/` — aplicação e motor · `tests/` — testes · `assets/` — identidade visual · `docs/` — documentação

Os testes físicos Explicit são separados das regressões e exigem autorização específica. Os comentários do código próprio estão em português brasileiro. A interface oferece três idiomas em Configurações → Idioma.

## Documentação

| Tema | Referências |
| --- | --- |
| Produto | [Plano mestre](docs/PRODUCT_MASTER_PLAN.md) · [Requisitos](docs/REQUIREMENTS.md) · [Hub](docs/PRODUCT_HUB_PLAN.md) |
| Engenharia | [Arquitetura](docs/ARCHITECTURE.md) · [Motor](docs/AUDIO_ENGINE_PLAN.md) · [WASAPI](docs/WASAPI_BACKEND_PLAN.md) · [Viabilidade](docs/FEASIBILITY.md) |
| Planejamento | [Implementação](docs/IMPLEMENTATION_PLAN.md) · [Efeitos futuros](docs/FUTURE_AUDIO_FEATURES.md) · [Próxima tarefa](NEXT_TASK.md) |
| Progresso | [Status](PROJECT_STATUS.md) · [Changelog](CHANGELOG.md) · [Revisões](REVIEW.md) |
| Qualidade | [Testes](docs/TEST_PLAN.md) · [Aceitação](docs/ALPHA_ACCEPTANCE.md) · [Auditoria](docs/GENERAL_CHECKUP.md) · [Recuperação](docs/RECOVERY_AUDIT.md) |
| Continuidade | [Guia para o próximo chat](CHAT_CONTINUITY.md) |
| Projeto | [Identidade visual](assets/branding/README.md) · [Licenças de terceiros](docs/THIRD_PARTY_NOTICES.md) · [Agentes](AGENTS.md) |

---

<div align="center">
  <img src="assets/branding/icons/WaveStudio-64.png" width="40" height="40" alt="Wave Studio" />
  <p><strong>Wave Studio</strong><br>Controle real. Áudio personalizado.</p>
</div>
