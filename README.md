# Wave Studio

<img src="assets/branding/icons/WaveStudio-256.png" width="144" height="144" alt="Logo oficial do Wave Studio" />

[Identidade oficial e recursos de distribuição](assets/branding/README.md). Original preservado em alta resolução; logo e ícone incorporados ao aplicativo, sem imagens externas obrigatórias.

Aplicação desktop para gerenciamento de áudio no Windows, independente de fabricante, com processamento e mixagem previstos no roadmap.

## Estado do projeto

A F2-I1 acrescenta uma [fundação sintética do motor](docs/AUDIO_ENGINE_PLAN.md), isolada em Audio/Engine e exercitada por testes: formato float32, conversão, fila circular, proteção e ciclo de vida. São 283 testes aprovados em Debug/Release, incluindo os 252 da base. Essa fundação ainda não controla dispositivos ou monitoramento contínuo e não modifica a janela Alpha. Funcionalidades reais são prioridade; redesign profissional fica para depois da consolidação. A prévia local existente foi preservada nesta etapa e seus testes físicos continuam pendentes conforme evidências.

F1-I6-METER acrescenta o medidor de entrada na aba de teste ao checkpoint técnico da Alpha. A prévia oferece seleção independente, volume/mute, balanço condicional, medidores reais, mixer de sessões, notificações, teste curto de microfone e preferências JSON. A aceitação física da v0.1.0 Alpha permanece pendente.

Na aba **Testar microfone**, clique em **Gravar teste**, fale e use **Parar** (limite de 10 segundos). Depois, com fones e volume baixo, clique em **Ouvir gravação**. A reprodução começa em nível digital de 20%, ajustável de 0 a 100%, sem alterar volume/mute do Windows. **Parar reprodução** interrompe a saída; **Limpar teste** apaga o trecho. Não há captura/reprodução automática nem monitoramento ao vivo. O áudio fica somente em memória e é descartado na troca/atualização de dispositivos, desconexão, suspensão ou fechamento.

O teste aceita mix mono/estéreo em PCM 16 bits ou float 32 bits, de 8 a 96 kHz. WASAPI compartilhado negocia a reprodução na saída selecionada; formato incompatível, acesso negado e mute geram mensagens. Nenhum destino alternativo é escolhido pelo teste. Microfone mono continua sem balanço L/R, com explicação próxima ao controle.

O grupo **Nível de entrada do microfone** mostra o pico real de amplitude, de 0 a 100%, da entrada selecionada. Ele reutiliza o medidor do Windows e não inicia gravação. Zero real, indisponibilidade e leitura atrasada têm textos distintos. O medidor não identifica voz, inteligibilidade ou zumbido; alguns drivers podem precisar de captura ativa para informar nível. Compare também durante **Gravar teste**, por sua ação explícita. A medição fica ativa em Dispositivos e Testar microfone e pausa nas outras abas ou ao minimizar.

A aba **Mixer de aplicativos** lista sessões reais da saída selecionada, com nome/processo quando disponível, PID auxiliar, estado, volume/mute e pico por instância. Edite Volume (%) e confirme com Enter, ou use o botão de mute. Picos são consultados a cada segundo, com silêncio, pausa, indisponibilidade e atraso identificados. A reconciliação por chave preserva linhas, foco e edição.

Verificação atual com 283 testes aprovados em Debug e Release, incluindo os 252 anteriores, e builds com zero erros/avisos. A prévia com identidade foi conferida na etapa anterior; nesta fundação isolada o executável publicado foi preservado. O ensaio de dez minutos pertence ao checkpoint F1-I6 anterior. O proprietário confirmou volume/mute e, após relato de zumbido/ausência de voz, informou sucesso da gravação/reprodução após ajustes próprios não especificados. A causa anterior não foi determinada. Subcasos A11, medidor A12 e demais critérios completos aguardam evidência física. Windows 10 é experimental e Windows 11 x64 continua pendente.

Versão v0.1.0 Alpha em desenvolvimento. Preferências duráveis de sessões aguardam identidade inequívoca. A Fase 1 aguarda aprovação explícita após os testes físicos.

## Documentação

- [Instruções para agentes](AGENTS.md)
- [Requisitos do produto](docs/REQUIREMENTS.md)
- [Arquitetura](docs/ARCHITECTURE.md)
- [Viabilidade, suporte e dependências](docs/FEASIBILITY.md)
- [Plano de implementação da Alpha](docs/IMPLEMENTATION_PLAN.md)
- [Plano de testes](docs/TEST_PLAN.md)
- [Status e inspeção do ambiente](PROJECT_STATUS.md)
- [Histórico de alterações](CHANGELOG.md)
- [Roteiro de aceitação e executável Release](docs/ALPHA_ACCEPTANCE.md)
- [Componentes e licenças da prévia](docs/THIRD_PARTY_NOTICES.md)
- [Referências de evolução de áudio](docs/FUTURE_AUDIO_FEATURES.md)

## Executável de teste local

A publicação atualizada F1-I6-METER fica em `artifacts/alpha-preview/win-x64/WaveStudio.exe`. Use a pasta completa e .NET Desktop Runtime 10 x64. Para validar com preferências isoladas, execute na raiz.

```powershell
$arquivoDeAceitacao = Join-Path $env:TEMP ('WaveStudio-aceitacao-' + [guid]::NewGuid().ToString('N') + '/settings.json')
& './artifacts/alpha-preview/win-x64/WaveStudio.exe' --settings-file $arquivoDeAceitacao
```

O [roteiro de aceitação](docs/ALPHA_ACCEPTANCE.md) contém SHA-256, comando de publicação e casos a relatar como aprovado, falhou ou pendente. O diretório artifacts é ignorado pelo Git, e a distribuição atual destina-se a testes locais.

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

As barras de Dispositivos mostram pico do Windows em percentual e dBFS. A barra de entrada em Testar microfone mostra percentual, disponibilidade e atraso. O mesmo worker consulta aproximadamente dez vezes por segundo e pausa fora das duas abas de áudio ou com a janela minimizada. RMS, LUFS e clipping exigem medições próprias futuras. A medição atual consulta os valores publicados pelo Core Audio, com processamento local.

Os controles refletem alterações externas por consultas em segundo plano a cada aproximadamente 600 ms. Balanço exige dois canais de volume e formato com máscara frontal L/R confirmada. O centro iguala os canais ao maior nível atual. Extremos atenuam o lado oposto e preservam esse nível-base. Mono e mapeamento desconhecido deixam o balanço desabilitado com explicação. Erros de escrita desabilitam o controle afetado até atualizar ou selecionar novamente. O volume é o controle do endpoint do Windows.

A suíte possui 271 casos de regras, simulações, armazenamento e WinForms STA e 12 integrações Windows. Captura/reprodução de teste e escritas automatizadas de áudio usam simulações. Nenhum teste automatizado aciona um stream do microfone real. Siga os [passos físicos dos incrementos](docs/TEST_PLAN.md).

## Preferências locais

- Seleções são salvas após sua escolha. A abertura, atualização e reconexão consultam os níveis reais do Windows.
- Em **Dispositivos**, use **Salvar valores atuais** para guardar volume, mute e balanço suportados no endpoint selecionado. Os valores salvos aparecem separados dos atuais.
- **Restaurar valores salvos…** mostra alvo, valores atuais e salvos e pede confirmação. Um aumento de volume recebe aviso. Mudança de seleção, suporte ou níveis após a prévia exige nova validação. Falha parcial mostra o resultado consultado.
- **Preferências locais** apresenta o arquivo e as ações de recuperação de backup e redefinição, ambas com confirmação. O arquivo padrão é `%LOCALAPPDATA%/WaveStudio/settings.json`.
- `settings.json.bak` recebe a configuração válida anterior. Um original corrompido fica preservado até recuperação explícita e é arquivado nessa operação. Versão incompatível bloqueia gravações, incluindo o fechamento.
- Arquivo maior que 1 MiB é protegido, com orientação de inspeção manual. Falha de validação ao salvar é informada e preserva a configuração válida. Balanço com canais em zero é informado como não observável, e a confirmação de unmute avisa sobre áudio audível.
- Duas instâncias usam exclusão durante escrita e detecção de conflito. Quando outra instância modifica o arquivo carregado, reabra o aplicativo para carregar essa versão.

Para experimentar com arquivo isolado em uma pasta temporária, informe um caminho absoluto após `--settings-file`.

```powershell
$arquivoDeTeste = Join-Path $env:TEMP ('WaveStudio-teste-' + [guid]::NewGuid().ToString('N') + '/settings.json')
dotnet run --project src/WaveStudio.Desktop -- --settings-file $arquivoDeTeste
```

O runner usa xUnit v3 4.0.3 e Microsoft Testing Platform 2.5.2. A variável acima desativa a telemetria do runner na sessão de PowerShell. As dependências de testes ficam no projeto de testes.
