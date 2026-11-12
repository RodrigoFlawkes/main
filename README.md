# Wave Studio

Aplicação desktop para gerenciamento de áudio no Windows, independente de fabricante, com processamento e mixagem previstos no roadmap.

## Estado do projeto

Fase 0 aprovada por revisão técnica do commit a3a3358. Fase 1, Incremento 1 implementado em 12/11/2026, com janela pt-BR, enumeração real de endpoints, identificação dos padrões, seleção independente de reprodução/captura e informações de disponibilidade e formato de mixagem.

Versão v0.1.0 Alpha em desenvolvimento. Os controles de volume/mute/balanço, sessões, medidores, hotplug automático e persistência permanecem nos próximos incrementos da Fase 1.

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

O executável fica em `src/WaveStudio.Desktop/bin/Debug/net10.0-windows/WaveStudio.exe`. A pasta de saída contém as dependências e requer .NET Desktop Runtime 10 x64. A seleção administra a preferência da janela. As consultas ao Windows são de leitura.

As listas incluem endpoints ativos e históricos, com ativos primeiro. Clique em **Atualizar dispositivos** após conectar/remover hardware ou alterar padrões no Windows. Campos de formato descrevem o mix compartilhado do Windows e seus bits de contêiner. Fabricante e modelo comercial dependem do nome exposto pelo sistema.

Foram aprovados 16 testes, sendo 14 de regras e mensagens e 2 de integração com Core Audio no Windows 10 local. Foram encontrados 67 endpoints com zero avisos de consulta. O executável abriu e os controles foram lidos por acessibilidade. A verificação visual e das interações permanece pendente devido a falhas da ferramenta de automação. Windows 10 mantém classificação experimental. Siga os [passos físicos do Incremento 1](docs/TEST_PLAN.md).

O runner usa xUnit v3 4.0.3 e Microsoft Testing Platform 2.5.2. A variável acima desativa a telemetria do runner na sessão de PowerShell. As dependências de testes ficam no projeto de testes.
