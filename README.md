# Wave Studio

Aplicação desktop para gerenciamento de áudio no Windows, independente de fabricante, com processamento e mixagem previstos no roadmap.

## Estado do projeto

Fase 0 aprovada por revisão técnica do commit a3a3358. O Incremento 1 da Fase 1 foi publicado no commit 60238ea. O Incremento 2 recebeu aprovação provisória para publicação e revisão técnica, com volume do endpoint de 0 a 100%, mute independente e balanço condicional para reprodução e captura, além da enumeração e seleção independentes.

Versão v0.1.0 Alpha em desenvolvimento. Sessões, medidores, hotplug automático e persistência permanecem nos próximos incrementos da Fase 1.

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

As listas incluem endpoints ativos e históricos, com ativos primeiro. Clique em **Atualizar dispositivos** após conectar/remover hardware ou alterar padrões no Windows. Campos de formato descrevem o mix compartilhado do Windows e seus bits de contêiner. Fabricante e modelo comercial dependem do nome exposto pelo sistema.

Os controles refletem alterações externas por consultas em segundo plano a cada aproximadamente 600 ms. Balanço exige dois canais de volume e formato com máscara frontal L/R confirmada. O centro iguala os canais ao maior nível atual. Extremos atenuam o lado oposto e preservam esse nível-base. Mono e mapeamento desconhecido deixam o balanço desabilitado com explicação. Erros de escrita desabilitam o controle afetado até atualizar ou selecionar novamente. O volume é o controle do endpoint do Windows.

Foram aprovados 56 testes, sendo 53 casos com simulações/regras e 3 de integração de leitura com Core Audio no Windows 10 local. Os 16 casos anteriores passaram novamente. A enumeração e a leitura de volume/mute dos padrões foram confirmadas em hardware real. A janela abriu e os controles foram lidos por acessibilidade. Operações físicas de volume/mute/balanço, comparação com o Windows, sincronização externa pela UI e avaliação visual permanecem pendentes. Windows 10 mantém classificação experimental. Siga os [passos físicos dos incrementos](docs/TEST_PLAN.md).

O runner usa xUnit v3 4.0.3 e Microsoft Testing Platform 2.5.2. A variável acima desativa a telemetria do runner na sessão de PowerShell. As dependências de testes ficam no projeto de testes.
