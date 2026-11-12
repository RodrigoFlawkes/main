# Wave Studio

Aplicação desktop para gerenciamento de áudio no Windows, independente de fabricante, com processamento e mixagem previstos no roadmap.

## Estado do projeto

F1-I5 implementada. Preferências locais em JSON preservam seleções independentes, modo **Seguir padrão multimídia**, IDs ausentes, posição/tamanho da janela e valores de endpoint salvos explicitamente. Volume, mute, balanço condicional, medidores reais, notificações de dispositivos e mixer de sessões continuam disponíveis.

A aba **Mixer de aplicativos** lista sessões reais da saída selecionada, com nome/processo quando disponível, PID auxiliar, estado, volume e mute por instância. Edite Volume (%) e confirme com Enter, ou use o botão de mute. A reconciliação por chave preserva linhas, foco e edição. O controle atua na sessão do Windows.

Verificação atual com 174 testes aprovados, incluindo os 101 anteriores, e build com zero erros/avisos. Integrações exercitam leitura Core Audio e abertura/reinício com armazenamento temporário isolado. Escritas de áudio automatizadas usam simulações. Resposta à fala/música, hotplug físico, suspensão/retomada, restauração audível, layout e Windows 11 x64 permanecem pendentes. Windows 10 é experimental.

Versão v0.1.0 Alpha em desenvolvimento. F1-I6 aguarda nova orientação para validação física/visual e entrega da Alpha. Preferências duráveis de sessões e medidores individuais contínuos seguem pendentes de identidade inequívoca e gerenciamento de ciclo de vida/custo.

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

A suíte possui 165 casos de regras, simulações, armazenamento temporário e WinForms STA, além de nove integrações Windows de leitura/execução isolada. O executável abriu e os novos controles foram reconhecidos por acessibilidade. Captura de tela e clique por geometria continuaram indisponíveis. Siga os [passos físicos dos incrementos](docs/TEST_PLAN.md).

## Preferências locais

- Seleções são salvas após sua escolha. A abertura, atualização e reconexão consultam os níveis reais do Windows.
- Em **Dispositivos**, use **Salvar valores atuais** para guardar volume, mute e balanço suportados no endpoint selecionado. Os valores salvos aparecem separados dos atuais.
- **Restaurar valores salvos…** mostra alvo, valores atuais e salvos e pede confirmação. Um aumento de volume recebe aviso. Mudança de seleção, suporte ou níveis após a prévia exige nova validação. Falha parcial mostra o resultado consultado.
- **Preferências locais** apresenta o arquivo e as ações de recuperação de backup e redefinição, ambas com confirmação. O arquivo padrão é `%LOCALAPPDATA%/WaveStudio/settings.json`.
- `settings.json.bak` recebe a configuração válida anterior. Um original corrompido fica preservado até recuperação explícita e é arquivado nessa operação. Versão incompatível bloqueia gravações, incluindo o fechamento.
- Duas instâncias usam exclusão durante escrita e detecção de conflito. Quando outra instância modifica o arquivo carregado, reabra o aplicativo para carregar essa versão.

Para experimentar com arquivo isolado em uma pasta temporária, informe um caminho absoluto após `--settings-file`.

```powershell
$arquivoDeTeste = Join-Path $env:TEMP ('WaveStudio-teste-' + [guid]::NewGuid().ToString('N') + '/settings.json')
dotnet run --project src/WaveStudio.Desktop -- --settings-file $arquivoDeTeste
```

O runner usa xUnit v3 4.0.3 e Microsoft Testing Platform 2.5.2. A variável acima desativa a telemetria do runner na sessão de PowerShell. As dependências de testes ficam no projeto de testes.
