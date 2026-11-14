<div align="center">
  <img src="assets/branding/icons/WaveStudio-256.png" width="144" height="144" alt="Logo oficial do Wave Studio" />

  <h1>Wave Studio</h1>

  <p><strong>Seu áudio. Seu microfone. Seu estúdio.</strong></p>
  <p>Central de gerenciamento, processamento e mixagem de áudio para Windows.<br>Independente de fabricante, projetada para jogos, comunicação, streaming, música e criação de conteúdo.</p>

  <p><code>v0.1.0 Alpha</code> · <code>Windows x64</code> · <code>C# / .NET 10</code> · <code>PT-BR</code></p>

  <p>
    <a href="#recursos-disponíveis">Recursos</a> ·
    <a href="#visão-do-produto">Visão do produto</a> ·
    <a href="#experimentar-a-prévia">Experimentar</a> ·
    <a href="#desenvolvimento">Desenvolvimento</a> ·
    <a href="docs/PRODUCT_MASTER_PLAN.md">Plano mestre</a>
  </p>
</div>

---

> [!IMPORTANT]
> **Projeto em desenvolvimento — Alpha experimental.** A prévia local já oferece controles reais do Windows, teste de microfone e equalização de uma gravação curta. O processamento global, microfone virtual, mesa digital completa e demais efeitos continuam em desenvolvimento. Não há instalador ou release pública nesta etapa.

## Sobre o Wave Studio

O Wave Studio nasce para reunir em uma única aplicação controles que normalmente ficam espalhados entre o Windows, softwares de periféricos e ferramentas de áudio. O objetivo é oferecer uma experiência completa inclusive a quem utiliza microfones e headsets sem software dedicado, respeitando as capacidades reais de cada dispositivo.

Três princípios orientam o desenvolvimento:

- **Independência de fabricante:** suporte por meio de interfaces documentadas do Windows, sem restringir a utilização a uma marca.
- **Controle e privacidade:** processamento local, ações explícitas para captura e reprodução, sem envio de áudio ou alterações silenciosas de drivers.
- **Qualidade e desempenho:** funcionalidades verificáveis, motor modular, prevenção de feedback e picos, estabilidade e otimização antes do acabamento visual.

## Recursos disponíveis

| Recurso da prévia | O que já oferece |
| --- | --- |
| Dispositivos | Seleção independente de entrada e saída, identificação de padrões, formatos e estados |
| Controle de áudio | Volume, mute e balanço L/R quando o endpoint suporta |
| Mixer de aplicativos | Sessões reais do Windows, volume/mute individual e pico por instância |
| Medidores | Nível real de entrada e saída; distinção de silêncio, indisponibilidade e atraso |
| Teste de microfone | Gravação de até 10 segundos em memória, reprodução explícita, stop e limpeza |
| Equalizador de prévia | Dez bandas reais, preamp, bypass, reset e comparação original/processado |
| Preferências | Seleções, valores de endpoint salvos explicitamente, backup, restauração e posição da janela |
| Conexão e recuperação | Notificações de dispositivos, atualização manual e invalidação de operações obsoletas |

**Atenção:** o EQ atual atua apenas na reprodução do trecho gravado dentro do Wave Studio. Não modifica o áudio de jogos, navegador, Discord ou OBS. O teste de microfone não é monitoramento ao vivo. A aceitação física da Alpha ainda está pendente.

### Equalizador de 10 bandas — primeira demonstração audível

| Frequências | Ajustes |
| --- | --- |
| 31 · 62 · 125 · 250 · 500 Hz | −15 a +15 dB por banda |
| 1 · 2 · 4 · 8 · 16 kHz | −15 a +15 dB por banda |
| Preamp | −30 a 0 dB, com margem conservadora de segurança |

Filtros peaking reais e independentes por canal. Suporte inicial a gravações PCM16/float32, mono/estéreo, 44,1 e 48 kHz. O original permanece preservado para comparação A/B. O nível digital inicia em 20%; a proteção final é hard clip, não um limiter. Ganhos positivos podem reduzir o volume pela atenuação preventiva; não compense elevando o volume físico durante o teste.

[Detalhes técnicos e roteiro A/B →](docs/EQ_PREVIEW.md)

## Visão do produto

A interface definitiva será organizada em seis áreas principais. Os recursos abaixo representam **o produto planejado**, não funcionalidades já entregues integralmente.

| Área | Funcionalidades previstas |
| --- | --- |
| **Hub** | Meu Equipamento: catálogo visual expansível de headsets e microfones, imagens personalizadas, conexão, estado e bateria quando realmente disponível |
| **Áudio** | Seletor e teste de saída, EQ gráfico e paramétrico, presets, graves, agudos, dinâmica, reverb, áudio espacial, Surround 7.1 e Virtual Speaker Shifter |
| **Microfone** | Seletor e teste, EQ, boost, monitoramento, redução de ruído, isolamento de fala, compressor, gate, limiter, de-esser, saturação, AEC, AGC, efeitos de voz e Soundboard |
| **Mixer** | Mesa digital, faders, master, game/chat balance, canais por fonte, mix pessoal/transmissão/chat/gravação, matriz de roteamento e gravação multifaixa |
| **Reparos** | Diagnóstico de som, dispositivos e drivers; correções específicas, seguras e confirmadas pelo usuário |
| **Config** | Temas claro/escuro/alto contraste/seguir Windows, cores, wallpaper, animações opcionais, idioma, inicialização, bandeja, perfis e atalhos |

O acesso **Apoiar** ficará discretamente no rodapé da barra lateral, com contribuição voluntária futura, sem bloquear funcionalidades.

### Expansão planejada

- Equalização independente de reprodução e captura, presets editáveis, importação/exportação e estudo de compatibilidade com configurações Equalizer APO e AutoEQ.
- Tratamento vocal modular, voz grave natural, alteração de pitch e formantes, correção de afinação, eco, vozes estilizadas e efeitos de ambiente.
- Soundboard com pads, arquivos próprios, gravação, atalhos e destinos selecionáveis.
- Surround virtual binaural/HRTF, posicionamento de canais e testes de alto-falantes.
- Roteamento real para Discord, OBS e jogos, com prevenção de loops e mix-minus.
- Estudo de hospedagem de plugins VST3, conforme viabilidade, segurança e licenciamento.

Compatibilidade de bateria, formatos, surround e recursos específicos depende do dispositivo, APIs e validação. A identidade visual selecionada não altera o hardware ou o DSP.

[Conhecer o plano mestre completo →](docs/PRODUCT_MASTER_PLAN.md)

## Roadmap

| Etapa | Situação |
| --- | --- |
| 0 — Arquitetura e viabilidade | Documentada |
| 1 — Base Windows e Alpha | Implementada; aceitação física pendente |
| 2 — Motor de áudio | Em desenvolvimento; buffers, resampling, clocks simulados e consulta WASAPI implementados |
| Prévia EQ | Implementada; escuta A/B pendente |
| 3 — Integração externa de áudio | Planejada; depende do transporte nativo |
| 4–5 — Mesa digital, barramentos e roteamento | Planejadas |
| 6–7 — Equalização avançada e tratamento vocal | Planejadas |
| 8–9 — Soundboard e alteração de voz | Planejadas |
| 10 — Surround e espacialização | Planejada |
| 11 — Gravação, perfis e automação | Planejada |
| 12 — Otimização, compatibilidade e distribuição | Planejada |

O acabamento profissional da interface, catálogo visual e personalização serão consolidados após as funcionalidades essenciais. A próxima etapa deve sempre ser consultada em [NEXT_TASK.md](NEXT_TASK.md); a evidência mais recente está em [PROJECT_STATUS.md](PROJECT_STATUS.md).

## Compatibilidade

| Plataforma / conexão | Situação |
| --- | --- |
| Windows 11 x64 | Plataforma prioritária; validação física pendente |
| Windows 10 x64 | Experimental; ambiente utilizado no desenvolvimento |
| USB, P2/P3, HDMI/DP, Bluetooth e wireless 2,4 GHz | Escopo de compatibilidade conforme endpoints e suporte real do Windows |
| Interfaces XLR e equipamentos analógicos | Conforme a interface de áudio que os expõe ao Windows |
| ARM64, ASIO, modo exclusivo e multicanal avançado | Estudos/expansões futuras |

Não é necessário instalar um driver próprio para os controles básicos da prévia. Recursos futuros de roteamento e DSP global dependem de integração comprovada, segura e documentada. Recursos físicos como phantom power não são emuláveis por software.

## Experimentar a prévia

A compilação local Release x64 utiliza a pasta completa:

`artifacts/alpha-preview/win-x64/WaveStudio.exe`

Requer **.NET Desktop Runtime 10 x64**. Os artefatos locais não são versionados no Git. Não há link público de download nesta fase.

Para iniciar na raiz do repositório com preferências isoladas:

```powershell
$arquivoDeAceitacao = Join-Path $env:TEMP ('WaveStudio-aceitacao-' + [guid]::NewGuid().ToString('N') + '/settings.json')
& './artifacts/alpha-preview/win-x64/WaveStudio.exe' --settings-file $arquivoDeAceitacao
```

### Testar o microfone e o EQ

1. Em **Dispositivos**, selecione microfone e saída.
2. Em **Testar microfone**, clique em **Gravar teste**, fale e pare.
3. Com fones e volume baixo, clique em **Ouvir gravação**.
4. Marque **Ativar EQ nesta gravação**, corte 1 kHz em −9 dB e clique em **Ouvir com EQ**.
5. Compare o mesmo original, teste bypass/reset e clique em **Limpar teste** ao terminar.

O teste curto aceita mono/estéreo PCM16 ou float32, 8–96 kHz; o EQ está disponível em 44,1/48 kHz. O áudio permanece em memória (até 10 s/8 MiB), é descartado em mudanças relevantes de dispositivo, suspensão e fechamento, e nunca é enviado a terceiros pelo aplicativo. Não há captura ou reprodução automática.

[Roteiro completo de aceitação →](docs/ALPHA_ACCEPTANCE.md)

## Desenvolvimento

**Tecnologias:** C#, .NET 10 LTS, Windows Forms, NAudio/Core Audio/WASAPI, configurações JSON versionadas e xUnit v3/Microsoft Testing Platform.

Na raiz do repositório, em PowerShell com o SDK .NET indicado em [global.json](global.json):

```powershell
dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build WaveStudio.slnx -c Debug --no-restore
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
dotnet test --solution WaveStudio.slnx -c Debug --no-build
dotnet run --project src/WaveStudio.Desktop
```

O executável de desenvolvimento fica em `src/WaveStudio.Desktop/bin/Debug/net10.0-windows/WaveStudio.exe`. Use a pasta completa com as dependências. Para testar configurações sem tocar nas preferências habituais, adicione `-- --settings-file $arquivoDeAceitacao` ao comando de execução.

```text
WaveStudio.slnx
├── src/
│   ├── WaveStudio.Audio/      # Core Audio, DSP e motor
│   └── WaveStudio.Desktop/    # Interface e preferências
├── tests/WaveStudio.Tests/   # Regras, simulações, STA e integrações
├── assets/branding/          # Identidade oficial
├── docs/                     # Requisitos, arquitetura e evidências
└── scripts/                  # Ferramentas de desenvolvimento
```

### Qualidade e validação

A entrega EQ-PREVIEW-1 registrou **495 testes aprovados em Debug e 495 em Release**, com builds sem erros/avisos. Consulte o [status atualizado](PROJECT_STATUS.md) para os resultados mais recentes. Testes automatizados e simulações não equivalem à validação de áudio físico. O backend WASAPI contínuo ainda não está integrado à interface; a lacuna de precisão do relógio nativo está documentada.

As correções de ciclo de vida e encerramento estão priorizadas na [auditoria geral](docs/GENERAL_CHECKUP.md). Não iniciar transporte contínuo antes de revisar segurança, relógios, formatos, quiescência e validação física.

<details>
<summary><strong>Detalhes operacionais da Alpha</strong></summary>

- A seleção de dispositivo na janela não altera automaticamente o padrão do Windows. A abertura consulta níveis existentes, e as alterações de volume/mute são explícitas.
- Balanço exige dois canais e máscara frontal L/R confirmada. Centro iguala ao maior nível atual; extremos atenuam o lado oposto. Mono/mapeamento desconhecido deixam o controle desabilitado.
- Medidores de endpoint são consultados aproximadamente a cada 100 ms e pausam fora das abas relevantes ou ao minimizar. Sessões do mixer usam medição a cada 1 s. Alterações externas nos controles são consultadas aproximadamente a cada 600 ms.
- O medidor mostra amplitude, não detecta inteligibilidade, voz ou zumbido. Alguns drivers podem exigir captura ativa para expor o pico.
- Sessões de aplicativos são identificadas por instância; persistência durável por identidade ainda está pendente. Confirme edição de volume com Enter.
- Em Preferências locais, o arquivo padrão é `%LOCALAPPDATA%/WaveStudio/settings.json`. A restauração de níveis exige confirmação e revalidação de alvo/suporte. Backup, corrupção, conflitos entre instâncias, versão incompatível e arquivos acima de 1 MiB recebem tratamento defensivo.

</details>

## Documentação

| Documento | Finalidade |
| --- | --- |
| [Plano mestre](docs/PRODUCT_MASTER_PLAN.md) | Visão completa e funcionalidades futuras |
| [Requisitos](docs/REQUIREMENTS.md) · [Hub e personalização](docs/PRODUCT_HUB_PLAN.md) | Especificações e navegação |
| [Arquitetura](docs/ARCHITECTURE.md) · [Viabilidade](docs/FEASIBILITY.md) | Decisões técnicas, suporte e limites |
| [Motor de áudio](docs/AUDIO_ENGINE_PLAN.md) · [WASAPI](docs/WASAPI_BACKEND_PLAN.md) | DSP, formatos, resampling, relógios e transporte |
| [Evolução de áudio](docs/FUTURE_AUDIO_FEATURES.md) | Efeitos, Equalizer APO, AutoEQ e plugins |
| [Plano de implementação](docs/IMPLEMENTATION_PLAN.md) · [Próxima tarefa](NEXT_TASK.md) | Ordem de desenvolvimento |
| [Status](PROJECT_STATUS.md) · [Changelog](CHANGELOG.md) · [Revisões](REVIEW.md) | Histórico e evidências |
| [Testes](docs/TEST_PLAN.md) · [Aceitação](docs/ALPHA_ACCEPTANCE.md) | Verificações automáticas e físicas |
| [Identidade visual](assets/branding/README.md) · [Avisos de terceiros](docs/THIRD_PARTY_NOTICES.md) | Marca e licenças |
| [Instruções para agentes](AGENTS.md) | Regras de desenvolvimento |

---

<div align="center">
  <img src="assets/branding/icons/WaveStudio-64.png" width="40" height="40" alt="Wave Studio" />
  <p><strong>Wave Studio</strong><br>Construído por etapas. Funcionalidade real antes da aparência.</p>
</div>
