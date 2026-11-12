# Status do Wave Studio

Atualizado em 12/11/2026.

## Estado atual — F1-I5 executada

Árvore inicialmente limpa, main sincronizada por git pull --ff-only até 4e2d690. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, requisitos, arquitetura, planos, status, NEXT_TASK.md e REVIEW.md lidos. A revisão mais recente é F1-I4 sobre b63ef63. Implementação, commit e push desta etapa autorizados pelo proprietário. F1-I6 e Fase 2 aguardam nova orientação.

| Categoria | Resultado atual |
| --- | --- |
| Implementado | JSON por usuário com schemaVersion 1, seleções/modos independentes, preferência ausente, janela validada, valores opcionais por endpoint, salvar/restaurar/reset/recuperar explícitos |
| Proteção de dados | I/O serializado fora da UI, temporário/flush/substituição com backup, conflito entre instâncias, corrupção e versão incompatível preservadas, arquivo inválido arquivado em recuperação explícita |
| Segurança de áudio | Carga/refresh/retorno consultam níveis atuais. Restauração exige prévia, confirmação e revalidação de ID, geração, disponibilidade, suporte e níveis, com leitura após falha parcial |
| Revisão F1-I4 | Falha terminal/freshness do worker, retomada/ciclo de vida com mesmo ID, recuperação manual, API de preferência por ID, fallback ativo e reconciliação pendente do mixer |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 174 aprovados, zero falhas e zero ignorados. 165 casos de regras/simulações/arquivos/STA e nove integrações Windows, incluindo os 101 anteriores |
| Integração | Dois ciclos de abertura/fechamento da janela em armazenamento isolado, seleção ausente preservada e fechamento mantendo bytes de arquivo protegido. Leituras reais de medidores e inscrições aprovadas |
| Execução/acessibilidade | Executável com arquivo temporário iniciou. Acessibilidade reconheceu Preferências locais, seleção ausente e ações. Captura visual e clique por geometria falharam |
| Limitações | Preferências de sessões aguardam identidade durável inequívoca. Medidores contínuos por sessão aguardam avaliação de ciclo de vida/custo na F1-I6 |
| Testes físicos/visuais | Restauração audível, fala/música, USB/padrões/retomada, dois aplicativos, diálogos, DPI, estabilidade prolongada e Windows 11 pendentes |
| Compatibilidade | Windows 10 Pro 22H2 experimental. Windows 11 x64 prioritário e pendente de validação |
| Próximo passo | Revisão técnica da F1-I5 e orientação para validação da F1-I6 |

### Decisões e verificações da F1-I5

- Configuração padrão em %LOCALAPPDATA%/WaveStudio/settings.json. Caminho injetável e --settings-file absoluto permitem ensaios isolados. As verificações desta etapa usaram arquivos temporários próprios e consultas de áudio. Escritas de áudio automatizadas ocorreram em simulações.
- Seleções são salvas após gesto explícito/modo. Valores de volume/mute/balanço usam Salvar valores atuais. Polling, notificações e medidores atualizam o estado atual. Posição/tamanho são salvos no fechamento com ajuste à área útil na carga. Preferências de ID ausente sobrevivem ao reinício e são exibidas como indisponíveis.
- SettingsStore usa System.Text.Json, schemaVersion 1, validação estrita, UTF-8/BOM, limites de tamanho/estrutura/IDs/valores e defaults seguros em memória. A primeira versão aguarda versões anteriores conhecidas para justificar migração.
- Escrita usa exclusão por handle de settings.json.lock e hash do conteúdo carregado, comparado antes de gravar e imediatamente antes de substituir. Transações locais são serializadas e pedidos da UI são coalescidos. Um conflito bloqueia sobrescrita e orienta reabrir. Temporário exclusivo na mesma pasta, Flush(true), File.Replace com backup válido e remoção do temporário próprio. Recuperação/reset arquiva original inválido antes de substituição. Falhas e cancelamento foram exercitados em diretórios temporários.
- Versão incompatível permanece byte a byte, inclusive em save/reset/close. Corrupção aguarda recuperação/reset explícitos, com backup validado. Principal ausente com backup válido também aguarda escolha. Limite de 1 MiB protege alocação, e arquivos maiores exigem inspeção externa preservando o original.
- EndpointRestorePlan captura prévia e geração. Confirmação mostra ID e níveis, avisa aumento e começa com Não selecionado. Serviço revalida identidade/geração, suporte e mudanças externas. Mute ligado vem primeiro e unmute por último. Falha interrompe sequência e mostra consulta real, com possível aplicação parcial. Chamadas nativas iniciadas podem terminar no alvo original.
- Worker publica falha terminal/indisponibilidade e picos antigos expiram após dois segundos na apresentação. Recover invalida leitura pendente imediatamente. Eventos de ciclo de vida, retomada e manual invalidam gerações/erros mesmo com ID igual. Metadados preservam controles. Atualização manual solicita reconstrução e o retorno do catálogo apenas reconcilia, evitando ciclo. Mixer mantém pedido de leitura recebido durante operação anterior.
- Identidade persistente de sessão foi avaliada. GetSessionIdentifier pode ser compartilhado por instâncias, e a camada atual identifica instâncias efêmeras/PID. Correspondência durável inequívoca permanece pendente. A UI informa indisponibilidade dessa preferência. Medição contínua por sessão segue para avaliação de fontes por instância e orçamento na F1-I6, mantendo a leitura pontual atual.
- dotnet restore WaveStudio.slnx --locked-mode aprovado. dotnet build WaveStudio.slnx -c Debug --no-restore aprovado com zero erros e zero avisos. dotnet test --solution WaveStudio.slnx -c Debug --no-build aprovou 174 testes, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Diagnósticos de desenvolvimento corrigidos incluíram CS8602 no botão de preferência, xUnit1051 por ausência de tokens de cancelamento e CS0411 na interseção de retângulos do teste. Dois testes de status consultavam a janela antes do evento Shown, corrigidos para aguardar inicialização. Suíte final aprovada.
- Integração específica observou 20 snapshots em 2,21 s, picos zero em ambas as direções e inscrição ativa. Consulta média de 0,090 ms, CPU do runner com delta de 46,9 ms e working set de 61.886.464 a 63.168.512 bytes. Quatro sessões reais tinham pico zero na leitura pontual. A amostra descreve esta máquina e período curto.
- Executável iniciou com --settings-file temporário e acessibilidade reconheceu os novos controles e a preferência ausente. A captura retornou window capture timed out e o clique coordinate input geometry is unavailable. Uma tentativa inicial de janela em isolamento de processos ficou inacessível, exigindo encerramento apenas dos processos próprios de teste. A verificação final utilizou janela no desktop e arquivo temporário novo. O encerramento normal gerou JSON com modos automáticos e geometria, e o reinício exibiu Preferências carregadas por acessibilidade. A janela final foi encerrada normalmente. Validação visual e operação interativa completa continuam pendentes.
- Produção nova em EndpointPreferenceRestore.cs, MainForm.Preferences.cs e Desktop/Settings (StudioSettings.cs, WindowPlacement.cs, SettingsStore.cs). Testes novos em SettingsStoreTests, SettingsValidationTests, EndpointPreferenceRestoreTests, RecoveryRegressionTests e PreferencesUiTests, além da regressão em MixerGridTests. Nenhum pacote novo. README, changelog, arquitetura, planos e status atualizados. git diff --check aprovado. Busca por padrões comuns de credenciais, dados sensíveis e artefatos sem correspondências no conteúdo versionável, com bin/obj ignorados e links locais válidos. AGENTS.md, docs/REQUIREMENTS.md, NEXT_TASK.md, REVIEW.md, configurações de build/SDK e lock files conferidos contra 4e2d690 e preservados. Publicação autorizada com commit descritivo e push normal, seguida de conferência da árvore limpa e sincronização com origin/main.

### Testes físicos pendentes da F1-I5

Anotar e restaurar os níveis originais do Windows e aplicativos. Testar salvar níveis atuais, reiniciar com seleções/modos preservados, prévia/cancelamento e restauração confirmada em hardware, incluindo mono/suporte ausente, alteração externa, troca/desconexão e falha parcial. Ensaios de corrupção/backup/versão futura/concorrência devem usar arquivo temporário via --settings-file. Conferir USB/padrões/retomada, áudio ativo e dois aplicativos, consumo prolongado, 1366×768, DPI 100/150/200% e Windows 11 x64. Procedimentos em docs/TEST_PLAN.md. A aceitação física da Alpha permanece pendente.

## Histórico — F1-I4 executada

Repositório limpo inicialmente, sincronizado por fetch e fast-forward até b4a6223 em main. Remoto confirmado como https://github.com/Wave-Universal-Studio/main.git. AGENTS.md, requisitos, arquitetura, plano, testes, NEXT_TASK.md e REVIEW.md foram lidos. A revisão mais recente é F1-I3. O proprietário autorizou implementação, commit e push da tarefa F1-I4. Persistência da F1-I5 aguarda nova orientação.

| Categoria | Resultado atual |
| --- | --- |
| Implementado | Medidores reais dos endpoints, notificações de dispositivos/padrões, recuperação, seguir padrão por direção e correções do mixer |
| Ciclo de vida | Worker MTA persistente proprietário de fontes e inscrição, snapshots por geração, descarte ordenado e callback breve |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 101 aprovados, zero falhas e zero ignorados, incluindo os 77 anteriores. 95 casos de regras/simulações/grid STA e seis integrações Windows de leitura |
| Integração real | Reprodução e captura com pico zero na amostra, inscrição automática ativa e descarte aprovado. Cinco sessões com medidor disponível e pico zero na leitura pontual |
| Execução | Executável abriu e encerrou normalmente. Acessibilidade reconheceu barras, silêncio, seguir padrão, 67 endpoints e detecção ativa. Captura visual retornou timeout em duas tentativas |
| Medidores por sessão | API avaliada pontualmente. Exibição contínua adiada para avaliação do custo e gerenciamento persistente por instância |
| Testes físicos | Resposta a fala/música, hotplug USB, padrões externos, suspensão/retomada, escrita de controles, dois aplicativos, DPI e estabilidade prolongada pendentes |
| Compatibilidade | Windows 10 Pro 22H2 experimental. Windows 11 x64 ainda pendente |
| Próximo passo | Revisão técnica da F1-I4 e ensaios físicos |

### Decisões e verificações da F1-I4

- Pico consultado por AudioMeterInformation.MasterPeakValue do NAudio.Wasapi 3.1.0. APIs públicas e propriedade de wrappers conferidas na tag e no XML instalado. Percentual e dBFS têm silêncio tratado, rejeição de valores inválidos e estado de indisponibilidade. A leitura é local e consulta medidores publicados pelo Windows.
- Worker de aproximadamente 10 Hz mantém dois medidores abertos, pausa leituras quando ocultos e publica snapshots com IDs/revisão. Simulação de 11 ticks verificou duas aberturas e 22 leituras, com descarte na troca e encerramento na thread proprietária. Filas existentes mantêm objetos próprios e continuam serializando controles e sessões.
- Notificações de adição, remoção, estado, padrão e propriedades relevantes apenas sinalizam flag atômica. Coalescência usa 200 ms de quietude e máximo de 1.000 ms. Falha de inscrição é indicada e ativa fallback de catálogo/registro a cada cinco segundos. PowerModeChanged solicita reconstrução e consulta na retomada.
- Seleção explícita é mantida durante ausência e retorno. Modo inicial e checkboxes acompanham padrão multimídia por direção. Binding de listas preserva o modo automático. Refresh/hotplug/retomada usam leitura dos níveis existentes. Os comandos continuam associados ao alvo e à geração capturados.
- Grid reconciliada por SessionKey, com preservação de linha/célula e edição. Cancelamento, binding, índices e reescolha do mesmo ID são protegidos. Aplicação de comando solicita consulta imediata e mantém erro visível. Falhas de abertura/identidade de sessões isoladas permitem buscar o alvo confirmado. Falhas após confirmação propagam-se.
- Uma amostra específica de 20 snapshots em 2,21 s observou consulta média de 0,083 ms. O processo completo do runner apresentou delta de CPU de 31,2 ms e working set de 59.949.056 a 61.140.992 bytes. Valores descrevem esta máquina e uma amostra curta. A resposta a fala/música continua pendente.
- dotnet restore WaveStudio.slnx --locked-mode aprovado. Durante o desenvolvimento, uma restauração encontrou três NU1900 por restrição de rede na auditoria NuGet. Repetição com acesso à rede passou com auditoria e zero avisos.
- dotnet build WaveStudio.slnx -c Debug --no-restore aprovado com zero erros e zero avisos. dotnet test --solution WaveStudio.slnx -c Debug --no-build aprovou 101 testes, com TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Testes STA iniciais expuseram edição ainda ativa após CancelEdit e descarte da grid nessa condição. O encerramento e a troca agora finalizam a edição sob proteção de binding. Os três testes da grid passaram na suíte final.
- Arquivos novos de produto são AudioWatchModels.cs, AudioWatchEngine.cs, AudioWatchWorker.cs e CoreAudioWatchBackend.cs. Testes novos em AudioWatchTests.cs, HotplugSelectionTests.cs, MixerGridTests.cs e AudioWatchIntegrationTests.cs, com regressões adicionais em ApplicationSessionTests.cs. Atualizados os painéis, MainForm, seleção e serviço de sessões. Projeto de testes referencia Desktop para regressão STA, e seu lock registra essa referência. Versões NuGet e locks de Audio/Desktop preservados.
- Atualizados README, CHANGELOG, arquitetura, plano de implementação e plano de testes. AGENTS.md, docs/REQUIREMENTS.md, NEXT_TASK.md e REVIEW.md conferidos contra b4a6223 e preservados integralmente. git diff --check aprovado. Busca por padrões comuns de credenciais e nomes sensíveis sem correspondências no conteúdo versionável. bin/obj confirmados como ignorados. Links locais válidos e arquivos novos conferidos quanto a espaços finais e conflitos. Publicação autorizada com commit descritivo e push normal, seguida de conferência da árvore e sincronização com origin/main.

### Testes físicos pendentes da F1-I4

O proprietário deve anotar níveis, mute, balanço e padrões existentes e restaurá-los ao final de ensaios de escrita. Reproduzir áudio e falar por ação própria para conferir movimento e silêncio nas duas barras. Desconectar/reconectar USB com seleção explícita e confirmar retorno ao mesmo ID. Alterar padrões multimídia/comunicações pelo Windows e verificar modo automático independente e marcas reais. Suspender/retomar somente por ação do proprietário. Testar dois aplicativos, foco/edição/Enter/Esc e volume/mute por sessão, arraste/teclado dos controles, encerramento durante atividade e uso prolongado. Conferir 1366×768, DPI 100/150/200% e Windows 11 x64. Procedimento detalhado em docs/TEST_PLAN.md.

## Histórico — F1-I3 executada

NEXT_TASK.md e REVIEW.md foram lidos após sincronização por fetch e fast-forward até 4ab114d. O proprietário autorizou implementação, commit e push desta etapa. A tarefa F1-I3 foi executada, incluindo os ajustes da revisão do Incremento 2. O Incremento 4 aguarda nova orientação.

| Categoria | Resultado atual |
| --- | --- |
| Implementado | Mixer real de sessões de reprodução, volume 0–100%, mute por instância, reconciliação e mudanças externas por polling |
| Identidade | Endpoint + identificador de instância, com revisão/cancelamento por seleção e revalidação imediatamente antes da escrita |
| Revisão do Incremento 2 | Gerações liberadas ao terminar operações, sliders confirmados no fim do gesto, janela com rolagem e limites da área útil, proteção da restauração contra mudanças externas detectáveis |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 77 aprovados, zero falhas e zero ignorados. 73 casos de regras/simulações e 4 integrações Windows de leitura, incluindo regressão dos 56 anteriores |
| Integração real | 4 sessões, todas inativas, zero avisos no endpoint de reprodução padrão. Duas instâncias de LEDKeeper2 com mesmo PID, FaceLift_x64 e Sons do sistema |
| Execução | Executável abriu, abas Dispositivos e Mixer de aplicativos reconhecidas por acessibilidade. Captura visual falhou por timeout |
| Testes físicos/visuais | Escritas, isolamento audível, sincronização externa pela UI, arraste contínuo, teclado/roda do mouse, 1366×768 e DPI elevado pendentes |
| Compatibilidade | Windows 10 Pro 22H2 experimental. Windows 11 x64 ainda pendente |
| Próxima etapa | Revisão técnica da F1-I3 e testes físicos. Incremento 4 pendente |

## Decisões e verificações da F1-I3

- O mixer possui uma aba própria e administra somente sessões do endpoint de reprodução selecionado. PID é metadado auxiliar. Instâncias do mesmo processo e sons do sistema continuam separados. Ausência de sessões é estado válido.
- Operações de sessão e de endpoint compartilham a fila serializada EndpointOperationQueue. Cada geração cancela pedidos antigos e libera seu CancellationTokenSource quando a última operação termina. Testes exercitam 2.000 trocas, incluindo uma operação bloqueada e descarte idempotente.
- Os sliders de endpoint exibem prévia durante Scroll e escrevem ao terminar o gesto de mouse/teclado, roda do mouse ou saída do controle com valor pendente. Atualizações de snapshots ficam protegidas contra gerar comandos. A interação contínua ainda exige conferência física.
- A janela limita tamanho à área útil e oferece rolagem na aba de dispositivos, com tamanho mínimo reduzido. A revisão visual de áreas menores e diferentes DPIs permanece pendente.
- O polling de endpoints permanece em 600 ms. O mixer usa 1.000 ms e suspende consultas periódicas quando a aba está oculta ou em edição. Medição de dez leituras completas observou médias de 1,40 ms no endpoint e 1,57 ms no mixer, nesta máquina. São tempos de parede de uma amostra curta, sem equivalência a orçamento de CPU ou garantia de desempenho. Notificações persistentes e otimização do ciclo COM ficam para o Incremento 4.
- Balanço mantém o limite do nível-base. A restauração compara os canais atuais com os níveis originais/tentados e evita sobrescrever uma alteração externa detectável. Desconexão ou concorrência entre chamadas nativas pode impedir restauração. A UI volta a consultar o estado real. Chamadas nativas já iniciadas podem terminar no alvo original.
- APIs da tag NAudio 3.1.0 e XML do pacote foram conferidas. AudioSessionManager.Sessions, GetSessionInstanceIdentifier, GetProcessID, IsSystemSoundsSession, State e SimpleAudioVolume são usados com descarte de cada sessão e do dispositivo/manager/coleção. Metadados ausentes ou processo encerrado usam nome genérico, sem afetar identidade.
- dotnet restore WaveStudio.slnx --locked-mode aprovado.
- dotnet build WaveStudio.slnx -c Debug --no-restore aprovado com zero erros e zero avisos.
- dotnet test --solution WaveStudio.slnx -c Debug --no-build aprovado com 77 testes, usando TESTINGPLATFORM_TELEMETRY_OPTOUT=1.
- Dois testes novos tiveram o diagnóstico xUnit2014 durante desenvolvimento e foram corrigidos para Assert.ThrowsAsync antes da verificação final.
- A integração real fez somente leituras e não iniciou reprodução. Escritas foram exercitadas com simulações. O teste específico de leitura encontrou quatro sessões reais com zero avisos e confirmou IDs únicos por endpoint/instância.
- A janela abriu e foi encerrada normalmente. A acessibilidade reconheceu as abas e controles existentes. A captura retornou window capture timed out, e a navegação automatizada não confirmou visualmente o conteúdo do mixer.
- AGENTS.md, docs/REQUIREMENTS.md, NEXT_TASK.md e REVIEW.md são preservados. Os registros abaixo são históricos e suas autorizações/pendências devem ser interpretadas conforme a entrega a que pertencem.
- git diff --check aprovado. Busca por padrões comuns de credenciais sem correspondências no conteúdo versionável. bin/obj conferidos como ignorados. Dependências e lock files preservados. Publicação autorizada pelo proprietário com commit descritivo e push normal, seguida de conferência da árvore e sincronização com origin/main.

### Testes físicos pendentes registrados na F1-I3

Anotar volume e mute de cada aplicativo e do endpoint antes do teste e restaurar ao final. Com dois aplicativos reproduzindo áudio por ação do proprietário, conferir identificação, alteração de uma sessão sem afetar a outra ou outro endpoint, mute/unmute, volume numérico com Enter/Esc, mudanças pelo mixer do Windows, criação/encerramento de processos e troca rápida de saída. Testar acesso negado, remoção/reconexão, arraste contínuo dos controles de endpoint e encerramento durante uma operação. Conferir layout e rolagem em 1366×768, 100/150/200% e Windows 11 x64. A aceitação física da Alpha continua pendente. Ver docs/TEST_PLAN.md.

## Histórico das entregas anteriores

As seções seguintes preservam os registros das Fases 0 e Incrementos 1–2. O estado atual da tarefa é o da seção F1-I5 acima.

### Etapa atual

Fase 0 publicada no commit a3a3358 e aprovada por revisão técnica informada pelo proprietário. Incremento 1 publicado no commit 60238ea após aprovação provisória e autorização de commit/push. Incremento 2 aprovado provisoriamente para publicação e revisão técnica, com commit/push autorizados. A v0.1.0 Alpha segue em desenvolvimento. A integração global tem caminho preferido por endpoints virtuais, com escolha concreta e prova física pendentes nas Fases 2–3.

| Categoria | Estado real |
| --- | --- |
| Implementado | Enumeração, padrões, seleção independente, informações, volume/mute e balanço condicional dos endpoints |
| Código de produto | Desktop e Audio, snapshots independentes de COM, operações serializadas e sincronização externa por polling |
| Compilado | Debug x64, zero erros e zero avisos |
| Testado automaticamente | 56 aprovados, zero falhas e zero ignorados. 53 casos de regras/simulações e 3 integrações de leitura Windows, incluindo os 16 anteriores |
| Execução real | Enumeração e leitura de volume/mute dos padrões confirmadas. UI abriu com controles lidos por acessibilidade |
| Testado fisicamente | Escritas de volume/mute/balanço, mudanças externas, comparação com Windows, hotplug e escala de tela pendentes |
| Próximo incremento | Aguardar revisão técnica e configuração do fluxo ChatGPT/Codex pelo GitHub antes do Incremento 3 |
| Dependências futuras | Integração virtual/licença/assinatura e prova externa pendentes |
| Plataforma prioritária | Windows 11 x64 ainda pendente de validação |

### Inspeção do ambiente

| Item | Resultado observado |
| --- | --- |
| Sistema | Windows 10 Pro 22H2 x64, build 19045.6466 |
| SDK | .NET 10.0.404, MSBuild 18.9.15 |
| Runtimes | Microsoft.NETCore.App 10.0.15 e 9.0.22, WindowsDesktop.App 10.0.15 e 9.0.22, AspNetCore.App 10.0.15 |
| Packs | Microsoft.WindowsDesktop.App.Ref, NETCore.App.Ref, NETCore.App.Host.win-x64 e AspNetCore.App.Ref presentes |
| Templates | `winforms`, `winformslib` e `winformscontrollib` disponíveis |
| Workloads | Nenhum instalado, templates e pack desktop já presentes |
| Git | 2.56.0.windows.4, branch main acompanhando origin/main |
| Estrutura inicial | README.md, .gitignore, AGENTS.md e docs/REQUIREMENTS.md, sem solução ou projetos |
| global.json | Ausente, fixação planejada ao criar a solução |

O suporte/ESU do Windows 10 local permanece desconhecido. A execução nesta máquina está classificada como experimental. A aprovação do produto no Windows 11 depende de ensaio nesse sistema. O inventário acima descreve o início da Fase 0. Agora existem WaveStudio.slnx, global.json e os três projetos previstos.

### Verificação do Incremento 1

O proprietário aprovou provisoriamente o relatório, autorizou a publicação com a mensagem `feat: initialize Wave Studio desktop and audio endpoint discovery` e autorizou o início do Incremento 2 após o push. A revisão prévia à publicação conferiu os arquivos implementados, os lock files, a ausência de padrões comuns de credenciais no conteúdo versionável e a exclusão de bin/obj pelo .gitignore. Permanecem pendentes os testes físicos e visuais registrados abaixo.

O commit 60238ea foi enviado a `https://github.com/Wave-Universal-Studio/main.git` e a sincronização com origin/main foi confirmada antes do início do Incremento 2. Foi usada somente no comando a identidade zzThiago e o endereço privado do GitHub já presentes no histórico.

- `dotnet restore WaveStudio.slnx` falhou inicialmente por acesso ao NuGet.Config fora do sandbox. A repetição autorizada restaurou os três projetos com sucesso. O restore em `--locked-mode` passou posteriormente.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou com zero erros e zero avisos.
- `dotnet test --solution WaveStudio.slnx -c Debug --no-build --output Detailed` passou com 16 testes aprovados, zero falhas e zero ignorados, usando `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` na sessão.
- Dependências diretas fixadas em NAudio.Wasapi `[3.1.0]` e xunit.v3 `[4.0.3]`. MTP 2.5.2 e transitivos registrados nos três packages.lock.json.
- A consulta real identificou 67 endpoints. O headset padrão multimídia foi exposto como Alto-falantes (3- USB Audio Device), com mix de 48 kHz, dois canais e contêiner de 32 bits. O microfone padrão expôs 48 kHz, um canal e contêiner de 32 bits. O padrão de reprodução para comunicações era outro endpoint já existente no host.
- O executável `src/WaveStudio.Desktop/bin/Debug/net10.0-windows/WaveStudio.exe` abriu. A ferramenta computer-use confirmou os textos e valores dos controles pela acessibilidade. Capturas falharam por timeout e clique por geometria indisponível. Inspeção visual e troca interativa ficaram pendentes. As regras de seleção independente e preservação após atualização foram verificadas automaticamente.
- As APIs NAudio da tag 3.1.0 foram consultadas e seu uso foi validado por build e execução real. Consultas utilizam COM somente em workers MTA, com descarte dentro da operação. Nenhuma chamada atual escreve configurações de áudio.
- AGENTS.md e docs/REQUIREMENTS.md foram relidos integralmente e preservados. Alterações desta etapa permanecem locais para revisão do proprietário.
- `git diff --check` passou. O Git informou somente avisos de normalização futura LF → CRLF. A janela de verificação permaneceu aberta após a ferramenta detectar interação do usuário, preservando o uso em andamento.

### Comandos e resultados desta etapa

- `Get-Content -Raw -Encoding UTF8` leu integralmente AGENTS.md e docs/REQUIREMENTS.md e inspecionou README e .gitignore. Arquitetura e status estavam ausentes inicialmente.
- `git status --short` e `git status --branch --short` identificaram AGENTS.md e docs/REQUIREMENTS.md já adicionados ao índice pelo usuário. Esses arquivos foram preservados.
- `git --version`, `dotnet --info`, `rg --files`, `git ls-files`, listagens de diretórios e consulta de registro confirmaram o inventário acima.
- `dotnet new list winforms` encontrou inicialmente acesso negado a `C:/Users/thiag/.dotnet`. A repetição autorizada concluiu com código 0 e listou os templates. O SDK inicializou sua configuração e informou instalação automática de certificado de desenvolvimento ASP.NET Core HTTPS. A saída também informou telemetria padrão da CLI. Esses efeitos pertencem à inicialização do SDK.
- Restore, build e test ficam pendentes de projetos, conforme [plano de implementação](docs/IMPLEMENTATION_PLAN.md). Nenhum teste manual de áudio foi executado.
- `git diff --check` passou. Verificação em PowerShell dos links locais em oito documentos encontrou zero destinos ausentes. A tentativa anterior em Python encontrou o executável inacessível, então a verificação foi concluída com PowerShell.

### Próxima etapa

Realizar os passos físicos dos Incrementos 1 e 2 descritos no plano de testes. Aguardar a revisão técnica do Incremento 2 e a configuração do novo fluxo de comunicação entre ChatGPT e Codex pelo GitHub. Ver [arquitetura](docs/ARCHITECTURE.md), [viabilidade](docs/FEASIBILITY.md) e [testes](docs/TEST_PLAN.md).

### Revisão para publicação do Incremento 2

O proprietário autorizou o commit e o push com a mensagem `feat: implement endpoint volume mute and stereo balance controls`.

- Revisados os controles, a leitura inicial, mute independente, limites de volume, validação de canais L/R, nível-base e restauração de balanço após falha parcial. A abertura e a seleção leem os valores existentes. Atualizações externas são consultadas por polling e aplicadas à UI sob proteção contra gerar comandos.
- Confirmados ID/revisão por seleção, cancelamento de pedidos antigos e descarte de respostas anteriores. Chamadas nativas já iniciadas podem terminar no endpoint original, conforme a limitação documentada na arquitetura.
- AGENTS.md e docs/REQUIREMENTS.md conferidos contra o commit a3a3358, com conteúdo original preservado. A arquitetura modular e os lock files foram preservados.
- Inventário versionável e busca de padrões comuns de credenciais revisados, sem credenciais ou arquivos sensíveis encontrados. Saídas bin/obj conferidas como ignoradas pelo Git, com inclusão de fontes, testes, documentação e arquivos de lock na entrega.
- Restore em modo locked aprovado. Build Debug x64 confirmado com zero erros e zero avisos. Os 56 testes passaram novamente, com zero falhas e zero ignorados. As integrações reais são de leitura, e as escritas foram exercitadas com simulações.
- Pendentes os testes físicos de escrita de volume/mute em entrada e saída, balanço audível e centralização, sincronização externa na UI, troca rápida e desconexão durante operação, recuperação/restauração e avaliação visual em diferentes escalas. Registrar e restaurar os níveis anteriores em cada teste físico. Windows 11 x64 permanece pendente e Windows 10 permanece experimental.
- `git diff --check` aprovado antes de preparar a publicação. Após o envio serão conferidos árvore de trabalho limpa e igualdade entre HEAD e origin/main.

### Verificação do Incremento 2

- Mesmas dependências e arquivos de lock do Incremento 1. `dotnet restore WaveStudio.slnx --locked-mode` passou.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou na verificação final com zero erros e zero avisos. `dotnet test --solution WaveStudio.slnx -c Debug --no-build` aprovou 56 testes, zero falhas e zero ignorados, com telemetria MTP desativada na sessão. A suíte inclui a regressão dos 16 testes anteriores.
- O primeiro build encontrou a janela do Incremento 1 ainda aberta e falhou ao copiar a DLL de áudio, com MSB3027/MSB3021 após dez avisos de nova tentativa. A janela foi fechada normalmente e a compilação seguinte passou. Um teste novo também usava `Assert.Throws` para método que retorna Task, corrigido para `Assert.ThrowsAsync` após os diagnósticos CS0619/xUnit2014.
- A integração de leitura observou reprodução em 100%, mute desligado e canais L/R em 1/1, com centro em 0. Captura em 100%, mute desligado e balanço indisponível por mono. Esses eram os valores existentes no host. Os testes reais fizeram consultas. Escritas foram exercitadas somente em endpoints simulados.
- A janela do Incremento 2 abriu. A acessibilidade confirmou sliders, números e botões, com balanço da captura desabilitado e explicação. A captura visual falhou novamente com `window capture timed out`, mantendo a avaliação visual e das interações como pendente. A janela de teste foi fechada normalmente.
- Implementados CoreAudioVolumeAccess, EndpointVolumeState/comandos, EndpointVolumeOperations, EndpointVolumeSession e EndpointControls. Testes novos em EndpointVolumeTests, EndpointVolumeSessionTests e FakeEndpointVolume, além da integração de leitura adicionada à suíte anterior.
- Controles sincronizam por polling de aproximadamente 600 ms. ID e revisão fixam o alvo de cada comando. Trocas de seleção cancelam pedidos anteriores, e a atualização de snapshots na UI fica protegida contra gerar escritas. Balanço exige estéreo FL/FR confirmado e tenta restaurar os níveis após falha parcial.
- A implementação recebeu aprovação provisória para publicação e revisão técnica. Windows 10 continua experimental. Windows 11 x64 e testes físicos de escrita continuam pendentes.
- `git diff --check` passou na conferência final. Os oito arquivos novos foram verificados sem espaços finais ou marcadores de conflito, e os links locais da documentação não têm destinos ausentes. A busca por padrões comuns de credenciais no conteúdo versionável não encontrou correspondências. O índice ficou vazio e o HEAD permaneceu em 60238ea, publicado antes do início deste incremento.

### Arquivos do Incremento 1

Foram criados 19 arquivos versionáveis. Saídas bin/obj são geradas e ignoradas pelo Git.

| Local | Arquivos criados |
| --- | --- |
| Raiz | WaveStudio.slnx, global.json, Directory.Build.props |
| src/WaveStudio.Audio | WaveStudio.Audio.csproj, packages.lock.json, DeviceSnapshot.cs, DeviceSelection.cs, AudioError.cs, CoreAudioDeviceService.cs |
| src/WaveStudio.Desktop | WaveStudio.Desktop.csproj, packages.lock.json, Program.cs, MainForm.cs, DevicePanel.cs |
| tests/WaveStudio.Tests | WaveStudio.Tests.csproj, packages.lock.json, DeviceSelectionTests.cs, AudioErrorTests.cs, CoreAudioIntegrationTests.cs |

Documentos atualizados — README.md, CHANGELOG.md, PROJECT_STATUS.md, docs/ARCHITECTURE.md, docs/FEASIBILITY.md, docs/IMPLEMENTATION_PLAN.md e docs/TEST_PLAN.md. Os links locais foram verificados sem destinos ausentes. Os 19 novos arquivos também foram inspecionados por PowerShell, sem espaços finais ou marcadores de conflito.

### Conferência para publicação da Fase 0

- Todos os documentos criados e modificados foram relidos. AGENTS.md e docs/REQUIREMENTS.md permanecem completos, com as seções e orientações originais, sem diferenças entre os arquivos de trabalho e o conteúdo previamente adicionado ao índice pelo usuário.
- O inventário contém somente documentos Markdown e .gitignore fora dos metadados internos do Git. A inspeção de arquivos, incluindo ocultos e ignorados, e a busca por padrões comuns de credenciais não encontraram credenciais, arquivos sensíveis ou temporários no conteúdo a publicar.
- O remoto origin de leitura e envio corresponde a `https://github.com/Wave-Universal-Studio/main.git`.
- O proprietário autorizou commit e push da Fase 0 com a mensagem `docs: initialize Wave Studio architecture and development roadmap`.
- A publicação foi concluída no commit a3a3358, com main sincronizada com origin/main naquela entrega. Posteriormente, o proprietário informou a aprovação técnica e autorizou o Incremento 1 da Fase 1.
