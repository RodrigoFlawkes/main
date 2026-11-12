# main
Plataforma universal de gerenciamento, processamento e mixagem de áudio para Windows, com foco em desempenho, personalização e compatibilidade entre dispositivos., independentemente do fabricante.

## Estado do projeto

Fase 0 documentada em 12/11/2026. A primeira versão funcional planejada é a v0.1.0 Alpha. O repositório está na etapa de arquitetura e planejamento.

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

## Execução futura

Após criar os projetos da Fase 1, o comando abaixo iniciará a aplicação.

```powershell
dotnet run --project src/WaveStudio.Desktop
```

Os comandos previstos de restauração, compilação e testes estão no plano de implementação. A aceitação da Alpha exige validação física dos controles no Windows. DSP global e microfone processado para aplicativos externos dependem das provas de integração das fases seguintes.
