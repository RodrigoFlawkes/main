# Arquitetura do Wave Studio

Decisões da Fase 0, em 12/11/2026, com atualizações dos Incrementos 1 e 2. Requisitos normativos em [REQUIREMENTS.md](REQUIREMENTS.md). As seções iniciais registram a implementação por incremento. As demais descrevem a arquitetura prevista para a Alpha e sua evolução.

## Implementação do Incremento 2

`CoreAudioVolumeAccess` encapsula o endpoint e seu `AudioEndpointVolume`. Acesso master escalar e mute usam `MasterVolumeLevelScalar` e `Mute`. O formato e `Channels.Count` confirmam o mapeamento para `Channels[index].VolumeLevelScalar`. Os objetos são criados, consultados e descartados dentro de uma operação em worker MTA. `MMDevice.Dispose` descarta o wrapper de volume, que cancela seu callback nativo interno. A UI recebe `EndpointVolumeState`, sem referências COM. [NAudio 3.1.0](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/AudioEndpointVolume.cs).

`EndpointVolumeSession` serializa as leituras e escritas de cada painel. Cada seleção possui ID fixo, revisão e token de cancelamento. Trocar a seleção invalida comandos enfileirados e respostas antigas, inclusive ao selecionar novamente o mesmo ID. Uma chamada nativa já iniciada pode terminar no endpoint original. Todos os comandos usam o ID capturado. Cancelamento é conferido antes das escritas, e uma operação de balanço parcialmente aplicada tenta restaurar os canais anteriores. O encerramento aguarda a enumeração e as filas de controle, descarta os recursos de vida útil e encerra os timers.

Sincronização externa utiliza polling de aproximadamente 600 ms por painel, com uma leitura por vez. A aplicação usa os valores lidos para atualizar os controles sob uma flag que impede gerar comandos. Escritas são originadas pelas ações de edição e botões. Os controles aguardam o retorno da operação e exibem a releitura do dispositivo. Uma revisão local adicional invalida respostas de leitura iniciadas antes de uma edição, inclusive se o retorno à UI ocorrer depois da escrita. Consultas e alterações de seleção usam somente leitura até uma ação explícita de edição.

O balanço atual é restrito a dois canais de volume e formato de mixagem com dois canais e `ChannelMask == 3`, confirmando FL/FR. Mono, multicanal e máscara ausente/desconhecida recebem explicação. A base é `max(L, R)` dos níveis de canal lidos imediatamente antes do comando. Posição positiva atenua L e negativa atenua R. Centro mantém ambos na base. A ordem de escrita começa pela atenuação quando há troca de lado. Em silêncio total, ambos permanecem em zero. O nível master usa a semântica do Windows, com leitura posterior dos canais para refletir o balanço efetivo.

Volume rejeita valores fora de 0–100 e números não finitos. Balanço rejeita valores fora de −100–100. Volume e mute têm confirmação por releitura, e o balanço confirma canais individualmente. Para acomodar quantização, a tolerância de confirmação é 0,02 no escalar. No balanço, também é rejeitada base lida superior à anterior em mais de 0,00001, admitindo apenas diferença numérica pequena. Falhas, controles ignorados pelo driver e canais acoplados geram erro e bloqueiam o controle afetado até atualização ou nova seleção. A restauração de uma atualização parcial também é conferida por leitura. Falha na restauração orienta conferir os níveis no Windows.

As duas chamadas de canal e a restauração são operações sequenciais. Desconexão física ou alteração simultânea por outro aplicativo pode impedir restaurar os valores anteriores. Esses cenários exigem teste físico por driver. As escritas foram testadas com endpoints simulados. No hardware real foram verificadas leituras e descarte. O funcionamento em modo exclusivo depende do suporte do dispositivo. [IAudioEndpointVolume](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume).

## Implementação do Incremento 1

A revisão técnica do commit a3a3358 foi aprovada pelo proprietário. A solução possui Desktop → Audio e Tests → Audio. Audio referencia somente `NAudio.Wasapi` 3.1.0, com `NAudio.Core` 3.1.0 e `System.Numerics.Tensors` 9.0.0 transitivos. Os três projetos têm lock files e destino `net10.0-windows`, x64. O destino técnico do framework permite compilar com as APIs utilizadas. A matriz de suporte do produto continua priorizando Windows 11, com Windows 10 experimental.

`CoreAudioDeviceService.QueryAsync` serializa consultas com `SemaphoreSlim` e usa `Task.Run` em worker MTA. Cada consulta cria e descarta seus próprios objetos COM na mesma thread, através de `using`. A UI recebe `DeviceCatalog`, `DeviceSnapshot` e `MixFormatSnapshot`, com valores copiados e listas somente de leitura. Como o Incremento 1 usa apenas consultas pontuais, a thread MTA dedicada e notificações persistentes descritas abaixo ficam para o incremento de hotplug.

A API NAudio 3.1.0 foi conferida no código da tag e validada por compilação e consultas reais. São usados `EnumerateAudioEndPoints`, `TryGetDefaultAudioEndpoint`, `MMDevice.ID`, `FriendlyName`, `State`, `CreateAudioClient` e `MixFormat`. A coleção e cada endpoint têm descarte próprio. `CreateAudioClient` exige descarte de cada cliente. [Enumerador](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDeviceEnumerator.cs), [coleção](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDeviceCollection.cs), [endpoint](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/CoreAudioApi/MMDevice.cs).

`DeviceSelection` guarda IDs independentes durante a execução. Inicialmente prefere o padrão multimídia. Uma preferência ausente continua registrada e é exibida como indisponível após atualizar. A seleção pode ser substituída explicitamente. Todas as chamadas Core Audio atuais são consultas. O botão Atualizar solicita nova enumeração e mantém as seleções. A consulta desabilita temporariamente os seletores e o botão, mantendo o loop de mensagens da janela disponível. Ao fechar, o token cancela a consulta entre endpoints e impede atualizar controles encerrados. Chamadas COM em andamento terminam antes do descarte local dos wrappers.

Erros por endpoint/direção geram avisos e preservam os dados obtidos dos outros dispositivos. Falha geral esvazia os controles e informa como tentar novamente. Metadados ausentes e endpoints inativos mantêm formato indisponível. O formato mostrado representa o mix do Windows. O campo de bits representa `BitsPerSample` do contêiner, com codificação exposta pelo wrapper.

## Base da primeira versão

C# com .NET 10 LTS, Windows Forms, destino `net10.0-windows` e processo x64. O SDK local é 10.0.404. Ao criar a solução, fixar essa linha em `global.json`, permitindo patches compatíveis, e configurar `AssemblyName` como `WaveStudio` para gerar `WaveStudio.exe`.

A solução `WaveStudio.slnx` começará com três projetos úteis.

| Projeto previsto | Responsabilidade |
| --- | --- |
| `src/WaveStudio.Desktop` | Janela pt-BR, apresentação, comandos, persistência JSON e composição dos serviços |
| `src/WaveStudio.Audio` | Modelos de endpoints/sessões, contratos e implementação Core Audio com NAudio, notificações e descarte |
| `tests/WaveStudio.Tests` | xUnit para regras de balanço, configurações e reconciliação de dispositivos/sessões |

Desktop referencia Audio. Audio permanece independente de WinForms. Persistência e regras de apresentação terão classes separadas e testáveis dentro de Desktop. Core, DSP e Mixer serão extraídos conforme houver implementação que justifique cada projeto.

## Controle de áudio da Alpha

Enumerar entrada e saída independentemente, incluindo ID, nome, estado e padrões de multimídia/comunicações. Exibir canais e formato de mixagem quando consultáveis. Metadados ausentes recebem indicação de indisponibilidade. A seleção na janela define o endpoint administrado pelo programa. Para alterar o padrão do sistema ou formato padrão, orientar pelas configurações nativas.

Usar as interfaces públicas `IMMDeviceEnumerator`/`IMMNotificationClient`, `IAudioEndpointVolume`, `IAudioMeterInformation`, `IAudioSessionManager2`, `IAudioSessionControl2` e `ISimpleAudioVolume`, através dos wrappers compatíveis da versão escolhida do NAudio. Conferir assinaturas no código dessa versão durante a implementação. O controle de endpoint e o controle por sessão têm alcances próprios e dependem do modo de áudio. [Documentação de volume](https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpoint-volume-controls), [sessões](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions).

Balanço exige canais L/R identificáveis e controle por canal funcional. Aplicar atenuação ao lado oposto, preservando o nível-base, com centro neutro. Mono, canais sem mapeamento e falhas de suporte deixam o controle desabilitado com motivo. Ler novamente os valores após alterações. Medidores usam picos reais expostos pela API, com indisponibilidade explícita. Medição de clipping do futuro motor exige acesso às amostras.

Sessões são identificadas pela instância em execução. PID serve como informação auxiliar, pois pode ser reutilizado. Tratar sons do sistema, múltiplas sessões por processo, processo encerrado e acesso negado. Preferências duráveis exigem identidade de aplicativo verificável e endpoint, evitando aplicar volume a outro processo. Agrupamento visual futuro preservará sessões individuais.

## Concorrência e recuperação

Um serviço de áudio com thread de trabalho MTA será responsável por COM, registros de eventos e ciclo de vida dos objetos. A UI STA recebe snapshots e atualiza controles por despacho para sua thread. Callbacks enfileiram notificações breves. Operações de volume e refresh são serializadas, com identificador de geração para descartar respostas de endpoints já substituídos.

Começar com leitura de medidores em torno de 10 Hz, ajustável após medir consumo. Coalescer hotplug e mudança de padrão. Ao remover um dispositivo, invalidar controles, cancelar tarefas, remover assinaturas e liberar referências. Manter a preferência do endpoint ausente, mostrar seu estado e permitir outra seleção explicitamente. Modo de seguir o padrão será uma preferência própria. Reenumerar sessões após criação/encerramento e após recuperação. Encerramento cancela trabalho, remove assinaturas e descarta recursos de maneira idempotente.

## Configurações e privacidade

Arquivo `%LOCALAPPDATA%/WaveStudio/settings.json`, com `schemaVersion`, seleções independentes, modo de seguir padrão, preferências por endpoint e estado da janela. Usar `System.Text.Json`. Validar intervalos e migrar versões conhecidas. Versão futura desconhecida deve gerar aviso e preservar o arquivo. Escrita serializada em arquivo temporário na mesma pasta, substituição atômica e backup da última configuração válida. Falha de escrita mantém a sessão operante e informa o usuário.

Na abertura, ler os volumes atuais do Windows. Persistir preferências de controles e valores escolhidos, com reaplicação de volumes somente por ação explícita de restaurar. Registrar e testar essa decisão de segurança. Nenhuma abertura deve aumentar volume inesperadamente. Logs locais contêm erros técnicos sem áudio ou dados sensíveis desnecessários. Processamento local e recursos de gravação somente mediante ação explícita.

## Caminho de integração global

Decisão arquitetural inicial para as Fases 2–3 — motor em espaço de usuário sobre WASAPI compartilhado, com endpoints virtuais fornecidos por integração documentada e assinada. A escolha do fornecedor depende de licença, disponibilidade e prova física. Essa dependência segue pendente. A Alpha utiliza os endpoints já existentes.

| Alternativa | Viabilidade e decisão |
| --- | --- |
| Core Audio de controle | Adequado para a Alpha, administra volume, sessões e medição |
| WASAPI shared | Base do motor futuro, usa formatos negociados e buffers, demanda medição de latência |
| Loopback de endpoint/processo | Útil para captura e análise, exige rota complementar para substituir o sinal ouvido |
| Endpoints virtuais | Caminho preferido para separar fluxos e entregar microfone processado a outros apps, depende de driver assinado e licença |
| APO | Alternativa para efeitos no pipeline do sistema, exige desenvolvimento nativo e integração/registro por endpoint, avaliar conflitos e modos de processamento |
| Driver próprio derivado de SysVAD | Alternativa de longo prazo, exige engenharia de driver, assinatura, compatibilidade e manutenção |
| WASAPI exclusive/ASIO | Casos avançados com acesso direto e possíveis desvios do processamento, suporte futuro condicionado a testes |

Loopback captura o fluxo reproduzido pelo endpoint. O pipeline externo precisa de entrada, processamento e entrega reais. [Loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording). APOs integram processamento ao sistema e requerem uma estratégia específica de instalação. SysVAD é uma base de estudo para driver. [APOs](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/implementing-audio-processing-objects), [SysVAD](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/).

Prova mínima de reprodução prevista — navegador → endpoint virtual de render → captura pelo motor → DSP de ganho mensurável → saída física. Prova de captura — microfone físico → motor/DSP → lado render de um cabo virtual → endpoint de captura correspondente → OBS/Discord selecionando esse endpoint. Medir sinal original e processado, duplicação, loops, latência e recuperação. Primeiro usar um par virtual por fluxo em teste. Game/Chat/Music/Media/System/AUX e saídas Virtual Microphone/Stream Mix/Recording Mix serão acrescentados conforme exigência de isolamento.

Antes de implementar muitos efeitos, registrar resultado reproduzível das duas provas ou o bloqueio específico. A solução comercial definitiva permanece condicionada a essas evidências. Instalações futuras requerem autorização. Drivers devem operar com Secure Boot, assinatura e integridade de memória habilitados, com desinstalação e recuperação verificadas.

## Evolução do motor

Na Fase 2, negociar formatos e converter para PCM float32, usando 48 kHz inicialmente quando compatível. Separar captura, DSP, barramentos e saída da UI. Definir efeitos com inicialização, processamento, reset, bypass, parâmetros, latência e descarte. Buffers pré-alocados, parâmetros suavizados e troca segura de estado. Callback sem disco, rede, alocação recorrente ou espera prolongada. Medir underruns/overruns, drift entre relógios, resampling, NaN/Infinity, CPU, RAM e latência real. Efeitos e monitoramento iniciam desligados, ganhos neutros e destinos externos dependem de configuração explícita.
