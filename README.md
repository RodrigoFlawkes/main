# Wave Studio

Aplicação desktop para gerenciamento de áudio no Windows, independente de fabricante, com processamento e mixagem previstos no roadmap.

## Estado do projeto

F1-I4 implementada. A aba **Dispositivos** apresenta picos reais dos endpoints de reprodução e captura, atualização automática por notificações do Windows e seleção independente com opção **Seguir padrão multimídia**. Seleções explícitas permanecem associadas ao dispositivo durante ausência e retorno. Volume, mute e balanço condicional administram os endpoints do Windows.

A aba **Mixer de aplicativos** lista sessões reais da saída selecionada, com nome/processo quando disponível, PID auxiliar, estado, volume e mute por instância. Edite Volume (%) e confirme com Enter, ou use o botão de mute. A reconciliação por chave preserva linhas, foco e edição. O controle atua na sessão do Windows.

Verificação atual com 101 testes aprovados, incluindo os 77 anteriores, e build com zero erros/avisos. Leituras reais observaram picos zero nos endpoints e em cinco sessões. Resposta dos medidores à fala/música, hotplug físico, suspensão/retomada, escritas e layout permanecem pendentes. Windows 10 é experimental, e Windows 11 x64 aguarda validação.

Versão v0.1.0 Alpha em desenvolvimento. Persistência JSON aguarda F1-I5 e nova orientação. Medidores individuais contínuos no mixer ficam para avaliação posterior de ciclo de vida e custo por sessão.

## Documentação

- [Instruções para agentes](AGENTS.md)
- [Requisitos do produto](docs/REQUIREMENTS.md)
- [Arquitetura](docs/ARCHITECTURE.md)
- [Viabilidade, suporte e dependências](docs/FEASIBILITY.md)
- [Plano de implementação da Alpha](docs/IMPLEMENTATION_PLAN.md)
- [Plano de testes](docs/TEST_PLAN.md)
- [Status e inspeção do ambiente](PROJECT_STATUS.md)
- [Histórico de alterações](CHANGELOG.md)

## Primeira versão funcional planejada

Janela Windows Forms em pt-BR, seleção independente de entrada e saída, identificação de padrões, volume/mute, balanço quando suportado, sessões reais com volume/mute, medidores, conexão/desconexão e configurações locais versionadas.

Windows 11 x64 é a plataforma prioritária. O ambiente inspecionado possui .NET SDK 10.0.404, runtime desktop 10.0.15 e Git 2.56.0, em Windows 10 Pro 22H2. A compatibilidade e a situação de suporte do Windows 10 serão avaliadas separadamente.

## Compilar, testar e executar

Na raiz do repositório, execute em PowerShell com .NET SDK 10.0.404 ou patch compatível da mesma faixa.

```powershell
dotnet restore WaveStudio.slnx --locked-mode
dotnet build WaveStudio.slnx -c Debug --no-restore
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
dotnet test --solution WaveStudio.slnx -c Debug --no-build
dotnet run --project src/WaveStudio.Desktop
```

O executável fica em `src/WaveStudio.Desktop/bin/Debug/net10.0-windows/WaveStudio.exe`. A pasta de saída contém as dependências e requer .NET Desktop Runtime 10 x64. A seleção administra a preferência da janela. A abertura e a seleção leem os níveis existentes. Ajustes nos sliders, valores numéricos e botões alteram explicitamente o endpoint selecionado.

As listas incluem endpoints ativos e históricos, com ativos primeiro. Notificações reconciliam conexões, estados, padrões e propriedades relevantes. **Atualizar dispositivos** oferece consulta manual. Falha na inscrição é indicada e ativa nova consulta a cada cinco segundos. Campos de formato descrevem o mix compartilhado do Windows e seus bits de contêiner. Fabricante e modelo comercial dependem do nome exposto pelo sistema.

As barras mostram pico do Windows em percentual e dBFS, com silêncio e indisponibilidade identificados. O worker consulta aproximadamente dez vezes por segundo e pausa a medição com a aba oculta ou janela minimizada. RMS, LUFS e clipping exigem medições próprias futuras. A medição atual consulta os valores publicados pelo Core Audio, com processamento local.

Os controles refletem alterações externas por consultas em segundo plano a cada aproximadamente 600 ms. Balanço exige dois canais de volume e formato com máscara frontal L/R confirmada. O centro iguala os canais ao maior nível atual. Extremos atenuam o lado oposto e preservam esse nível-base. Mono e mapeamento desconhecido deixam o balanço desabilitado com explicação. Erros de escrita desabilitam o controle afetado até atualizar ou selecionar novamente. O volume é o controle do endpoint do Windows.

A suíte possui 95 casos de regras, simulações e grid WinForms STA e seis integrações de leitura Core Audio. O executável abriu, e a acessibilidade reconheceu barras, silêncio, seleções e detecção automática ativa. A captura de tela retornou timeout. Siga os [passos físicos dos incrementos](docs/TEST_PLAN.md).

O runner usa xUnit v3 4.0.3 e Microsoft Testing Platform 2.5.2. A variável acima desativa a telemetria do runner na sessão de PowerShell. As dependências de testes ficam no projeto de testes.
