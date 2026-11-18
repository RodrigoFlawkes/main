# Backend WASAPI — preparação sem áudio e base isolada

## F2-I6B1-PROOF — resultado físico, 18/11/2026

Única tentativa autorizada após validação manual pós-formatação relatada pelo proprietário. Conferidos fechamento do app, isolamento da prova e entrada/saída únicas, ativas e de direção explícita. O apphost Release da implementação `bed126c` executou somente `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport` com `--explicit only --filter-method` e o nome completo do método. O runner retornou código 2 e registrou uma falha em 546 ms de execução total.

| Evidência | Captura | Render SILENT |
| --- | --- | --- |
| Mix exato | Float32 / 48 kHz / mono, máscara 4, 4 bytes/frame | Float32 / 48 kHz / LR, máscara 3, 8 bytes/frame |
| Capacidade / padding no snapshot | 1.056 / 480 frames | 1.056 / 1.056 frames |
| Período padrão / latência reportada | 100.000 / 0 × 100 ns | 100.000 / 0 × 100 ns |
| Initialize e Start | Confirmados, Start S_OK | Confirmados, Start S_OK |
| Packets adquiridos / liberados | 1 / 1 | 2 / 2 |
| Frames adquiridos / liberados | 480 / 0 (packet rejeitado) | 1.536 / 1.536 |
| Priming em frames | 0 | 1.056 antes do Start |
| GetBuffer / ReleaseBuffer / Stop | S_OK / S_OK / S_OK | S_OK / S_OK / S_OK |
| Flags, posição e QPC do pacote de captura | 0x1 DATA_DISCONTINUITY, 20.255.040, QPC 104.707.413.546 × 100 ns | Render submetido com SILENT |
| Leituras de IAudioClock / avanços validados | Sem leitura registrada / 0 | Sem leitura registrada / 0 |
| Release final / buffer em voo / erro de limpeza | Confirmado / não / nenhum | Confirmado / não / nenhum |

A falha de estágio `Capture` foi registrada como `InvalidOperationException`, sem HRESULT de falha. O pacote sinalizado foi liberado com zero frames antes que o código lesse seu PCM. A origem da descontinuidade permanece indeterminada. A janela do ticket durou 110.401 × 100 ns (11,0401 ms), com `DurationReached=false`, `MovementProven=false` e `TransportObserved=false`. O QPC do pacote não constitui evidência de IAudioClock em movimento. O FIFO teve capacidade de 4.096 frames, 0 produzidos, 0 lidos, 0 descartados e 480 de underrun interno; esses números e a latência reportada não medem latência fim a fim ou glitches no headset. Nenhum recurso ficou retido.

O relatório numérico ficou em `artifacts/wasapi-silent`, ignorado pelo Git, sem nomes/IDs de endpoints ou PCM. A tentativa terminou sem repetição, mudança de parâmetros ou alteração do código. A prova física requer revisão antes de nova autorização.

## F2-I6B1 — caminho nativo silencioso, 18/11/2026

A ponte de preparação foi reutilizada com `WasapiResourceScope`. O modo compartilhado continua com flags 0, duração/período 0 e GUID de sessão próprio. Os dois clientes revalidam o endpoint explícito e o formato antes do Start, consultam capacidade e padding efetivos e usam polling limitado a 1–10 ms conforme o período. A prova tem dois owners MTA estáveis, cada qual com seu dispositivo, cliente, clock e serviço de packets. [IDL do Windows SDK](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/Audioclient.idl).

| Interface COM | IID | Métodos após IUnknown |
| --- | --- | --- |
| IAudioCaptureClient | C8ADBD64-E71E-48A0-A4DE-185C395CD317 | GetBuffer, ReleaseBuffer, GetNextPacketSize |
| IAudioRenderClient | F294ACFC-3146-4483-A7BF-ADDCA7C260E2 | GetBuffer, ReleaseBuffer |

`PreserveSig` conserva HRESULTs. Endereços usam `nint`, contagens e flags usam `uint`, posição e QPC de captura usam `ulong`. A captura drena até 32 packets por iteração, lê silêncio sem tocar no ponteiro e libera o packet inteiro ou zero frames quando rejeitado. BUFFER_EMPTY dispensa ReleaseBuffer. Flags de descontinuidade, erro de timestamp e bits desconhecidos encerram a observação. O render pede capacidade menos padding e libera exatamente o tamanho solicitado com SILENT, inclusive quando ocorre cancelamento ou falha após a aquisição. PCM fica em memória privada. FIR e consumo da SPSC ocorrem após o ReleaseBuffer de cada owner. [Captura](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), [padding](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getcurrentpadding), [render](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiorenderclient-releasebuffer).

O render é primado por completo e inicia antes da captura. O coordenador habilita o ticket depois dos dois Starts. Stop invalida o ticket antes de acordar os owners. Cada owner encerra sua operação em voo, chama Stop se tentou Start e libera serviço → clock → cliente → endpoint/apartamento. O lease só é liberado após o join de ambos. Timeout conserva recursos/lease. Falha de Stop admite nova tentativa no mesmo owner durante descarte explícito. Falha de ReleaseBuffer ou de liberação COM conserva recursos/lease sem repetir a liberação incerta. O diagnóstico curto agora também conserva o lease quando completion foi observado e Dispose falha. [Contrato de Stop](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-stop).

Clocks são consultados após Start na própria sessão, preservando frequência, posição, QPC e HRESULTs brutos. QPC local serve para idade e duração. A aceitação exige pelo menos dois avanços validados por papel, janela concluída, captura e render pós-priming com frames liberados e contabilidade coerente após join. Esse checkpoint terminal confirma apenas o encerramento e as contagens observadas. Média temporal, barreira intermediária de eventos, calibração de granularidade e controle de drift do hardware ficam para F2-I6B2 após a prova silenciosa. Drops e underruns da fila são contadores internos. A capacidade e latência reportadas pelo cliente não medem latência fim a fim.

## F2-I6A — preparação nativa, 14/11/2026

Ponte mínima implementada em WasapiNativeInterfaces/NativeWasapiPreparationDevice, com coordenação em WasapiPreparation. NAudio.Core/Wasapi permanecem em3.1.0, sem dependências novas. A fonte fixada confirma que as interfaces COM do NAudio são internas e AudioClockClient.GetPosition trata−1, retornando true para S_FALSE/+1. Nenhum caminho público do wrapper expõe o HRESULT da mesma sessão. Escolhida interop mínima das interfaces públicas Windows, sem reflection, acesso privado, nova estimativa/FIR ou atualização de pacote. O bloqueio de representação do HRESULT está resolvido neste caminho; transporte ativo continua pendente.

### Auditoria ABI e propriedade

Conferidos o [wrapper NAudio fixado](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/CoreAudioApi/AudioClockClient.cs), [Audioclient.idl Microsoft](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/Audioclient.idl) e [mmdeviceapi.idl Microsoft](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/mmdeviceapi.idl). A ordem abaixo sucede os três slots de IUnknown; nenhuma chamada por índice adivinhado. Assinaturas completas necessárias à ordem foram declaradas, mas métodos de início/buffers não fazem parte da interface gerenciada de preparação.

| Interface / GUID | Ordem dos métodos próprios |
| --- | --- |
| IMMDeviceEnumerator / A95664D2-9614-4F35-A746-DE8DB63617E6 | EnumAudioEndpoints, GetDefaultAudioEndpoint, GetDevice, RegisterEndpointNotificationCallback, UnregisterEndpointNotificationCallback |
| IMMDevice / D666063F-1587-4E43-81F1-B948E807363F | Activate, OpenPropertyStore, GetId, GetState |
| IMMEndpoint / 1BE09788-6894-4089-8586-9A2A6C265AC5 | GetDataFlow |
| IAudioClient / 1CB9AD4C-DBFA-4C32-B178-C2F568A703B2 | Initialize, GetBufferSize, GetStreamLatency, GetCurrentPadding, IsFormatSupported, GetMixFormat, GetDevicePeriod, Start, Stop, Reset, SetEventHandle, GetService |
| IAudioClock / CD63314F-3FBA-4A1B-812C-EF96358728E7 | GetFrequency, GetPosition, GetCharacteristics |

HRESULT→int com PreserveSig; DWORD/UINT32→uint; REFERENCE_TIME→long; UINT64→ulong; ponteiros→nint; REFIID/LPCGUID→in Guid; ID de entrada LPWStr. GetId, GetMixFormat e closest usam CoTaskMem e são liberados no mesmo owner. Não há buffer PCM emprestado ou interfaces de captura/render. GUID da classe enumeradora BCDE0395-E52F-467C-8E3D-C4579291692E; CoCreateInstance/Activate com INPROC_SERVER, parâmetros nulos. Source generation .NET10 usa **ComObjectWrapper**, wrappers únicos StrategyBasedComWrappers, referências cruas balanceadas e FinalRelease apenas da instância própria. AllowUnsafeBlocks foi habilitado somente para o código gerado pelo SDK; nenhum ponteiro/vtable manual ou implementação de biblioteca copiada. [Opções do gerador](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshalling.cominterfaceoptions?view=net-10.0), [liberação de wrapper único](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshalling.comobject.finalrelease?view=net-10.0).

Task.Run executa uma operação síncrona inteira em MTA, sem await no owner COM. CoInitializeEx(MULTITHREADED) balanceia S_OK/S_FALSE com CoUninitialize. Identidade/direção/estado ativo são rechecados antes da ativação, antes da inicialização e depois da consulta; mix é comparado ao descritor escolhido previamente, validado e IsFormatSupported exige S_OK sem closest. Não há fallback. Initialize é shared, flags0, duração0/período0, formato nativo exato e GUID de sessão exclusivo. Sem AutoConvertPcm, SrcDefaultQuality, Loopback ou event callback. GetBufferSize é consulta de capacidade, **não** GetBuffer de áudio. [Activate](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdevice-activate), [Initialize](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize), [GetService](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getservice).

Clock obtido exclusivamente por GetService do cliente recém-inicializado. Ordem de liberação: clock→cliente/formato→endpoint/enumerador→apartamento→lease. Cancelamento é conferido entre chamadas; chamada COM em andamento não é abortável. O lease permanece enquanto essa chamada não retorna. Em falha de Release, filho e seus pais permanecem enraizados e o guard retido; não há retry cego em outra thread, force dispose ou promessa de recuperação. Uma falha assim exige revisão/reinício do processo; não foi provocada em hardware. Falhas HRESULT são reportadas com fase/código, sem ID ou conteúdo de áudio.

### Semântica do relógio

WasapiClockReading conserva ambos os HRESULTs e os três UINT64 integralmente. GetFrequency exige S_OK e frequência positiva. GetPosition S_OK→Accurate, S_FALSE(+1)→Uncertain; resultados negativos, positivos desconhecidos ou frequência inválida→Unavailable. Saídas de uma chamada falhada são apenas dados brutos sem validade. O overload de WasapiClockAdapter reutiliza exatamente as validações anteriores, com DeviceUnits, QPC100ns, identidade/geração e flags explícitas. Não converte posição absoluta para double nem usa Stopwatch/bytes processados como clock físico. [Precisão/S_FALSE e QPC](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition), [unidades de frequência](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency).

S_OK indica o resultado da leitura, não clock em execução. A preparação permanece parada; suas observações não alimentam o estimador/drift. Zero é preservado e não inventa progresso. S_FALSE e descontinuidade/timestamp inválido continuam invalidando o adaptador ativo e exigindo nova sessão; Silent não cria erro de timestamp. Flags de pacote não vêm de IAudioClock e não são fabricadas na prova parada.

### Prova autorizada sem áudio

Executada uma prova explícita própria em Debug, após verificar WaveStudio.exe fechado. Nome de captura “fifine” e saída “USB Audio Device” resolveram cada um um único ID ativo da direção requerida; o ID foi fixado no pedido e revalidado, sem seguir padrão/fallback. Relatório local numeric-only em artifacts/wasapi-preparation/native-preparation.json, fora do Git. Teste marcado Explicit: regressão comum não inicializa hardware, e nova execução exige autorização, variáveis opt-in e seleção explícita.

| Consulta real | Captura | Reprodução |
| --- | --- | --- |
| Mix exato | float32/48kHz, mono, máscara4 | float32/48kHz, LR, máscara3 |
| Período padrão / mínimo exclusivo |100.000 /30.000 ×100ns|100.000 /30.000 ×100ns|
| Buffer após Initialize |1.056 frames|1.056 frames|
| StreamLatency reportada |0 ×100ns|0 ×100ns|
| GetFrequency HRESULT / frequência |S_OK /192.000|S_OK /384.000|
| GetPosition HRESULT / posição |S_OK /0|S_OK /0|
| QPC100ns bruto |0|103.502.894.326|
| Inicializado / Start / recursos liberados |sim /não /sim|sim /não /sim|

1.056/48.000 equivale a22ms de capacidade alocada; isso e StreamLatency0 **não são latência fim a fim/zero latência**. Posição0/QPC0 da captura parada não comprova precisão em movimento. Nenhum Start, GetBuffer de captura/render, conteúdo gravado/reproduzido, mudança de volume/mute/padrão ou sessão mantida aberta. Sem prova de drift nativo, eventos, packets ou sincronização entre osciladores. Windows10 experimental; Windows11 pendente. A primeira tentativa encontrou EntryPointNotFound por opção ManagedObjectWrapper invertida; recursos foram limpos pelo finally. Corrigida para ComObjectWrapper e prova repetida com sucesso, sem contorno do contrato Windows.

### Interlock e desenho mínimo F2-I6B — somente planejamento

MicrophoneTestSession de produção e prova nativa usam TransportActivityGuard.Shared no mesmo processo. Engine mantém ShortDiagnostic desde antes da abertura até completion/descarte; Stop pendente ou exceção terminal não libera autorização prematuramente. Testes/fakes podem injetar guards isolados. Preparação usa NativePreparation e conserva lease até liberação confirmada. Não é mutex entre processos: a prova verifica ausência da prévia; uma futura arquitetura multiprocesso terá de tratar isso separadamente. Sem UI nova, republicação da prévia ou conexão do transporte fake ao hardware.

Para F2-I6B, após revisão, aceitação pertinente e autorização própria:

1. Workers MTA proprietários por papel, ciclo de vida/event handles definidos; GetService/Release no mesmo owner, GetBuffer/ReleaseBuffer de cada papel na mesma thread e sempre balanceados, sem atravessar await/callback emprestado. DSP/adaptativo pertence só ao produtor, consumidor à SPSC; recursos pré-alocados e sem I/O no callback.
2. Guard compartilhado desde preparação até stop/join/quiescência. DeliveryTicket/IDs/geração imutáveis, revalidação imediatamente antes de ReleaseBuffer/submissão; invalidação primeiro e stop fora do callback. Operação já submetida não pode ser revogada.
3. Render deve ser preparado e primado com silêncio real antes do primeiro Start; definir ordem de Start/Stop dos dois papéis e janela de autorização sem rejeitar priming necessário. Fila só entrega sinal após priming/relógios válidos e opt-in. Underrun gera silêncio/contador e interrompe/reprima segundo contrato; nenhuma mudança de volume Windows. Recalcular orçamento com buffer real1056, não assumir buffer480 ou priming1024 suficientes.
4. Watermark do fake não existe no WASAPI. Alternativa verificável: cada owner publica ledger cumulativo com sequência e intervalo de eventos concluídos; coordenador solicita uma barreira/pausa de controle, recebe confirmação dos dois owners sem aquisições/ReleaseBuffer em voo e captura um checkpoint comum. Recusar janela sem confirmação/ledger incompatível, sem reconstruir passado por snapshots ou atrasar callback aguardando controle. Definir a relação temporal entre aquisição, submissão e checkpoint antes de conectar o controlador; custo/pausa/clock obsoleto devem ser medidos, sem inventar watermark físico.
5. S_FALSE, flags de descontinuidade/timestamp error, mudança de frequência/identidade, QPC inválido e falta de frescor invalidam entrega imediatamente. Nova geração recria clocks/filtros e contabiliza drops/fila/priming. Nenhum fallback/restart automático. Timeout preserva recursos/lease até término confirmado, sem force dispose.

O desenho não é implementação ou prova de estabilidade ativa. F2-I6B/F3 continuam pendentes; EQ/perfis, headroom, FIR/SPSC e Alpha existentes preservados.

## Histórico — F2-I5

Adendo CHECKUP-1: Tick não acessa mais Buffer!.Value quando o snapshot falta/falha. Interrompe entrega com SnapshotUnavailable e média null; BufferSnapshotAvailable distingue ausência e últimos contadores conhecidos. Testes injetam falha/ausência e callbacks durante Start, que permanecem barrados pelo gate. Priming/start/watermark **continuam somente fake**, não autorização para conectar WASAPI ativo. Próxima preparação F2-I6A: HRESULT preciso na mesma sessão sem Start; F2-I6B posterior: guard compartilhado com diagnóstico, ownership/quiescência e checkpoint/priming nativos, revisão/aceitação/autorização. [Check-up](GENERAL_CHECKUP.md).

Implementação em 14/11/2026. Contratos internos em Audio/Engine, consulta real somente leitura e sessão de transporte **exclusivamente simulada**. Não há Initialize/Start nativo novo, vínculo com a UI, gravação, monitoramento contínuo ou ajuste de níveis/padrões do Windows. A Alpha continua em validação; Windows 10 experimental e Windows 11 x64 pendente. Este documento complementa os requisitos, sem alterar seu escopo.

## Auditoria das APIs fixadas

NAudio.Core e NAudio.Wasapi **3.1.0**, já fixados/locked, licença MIT. Fontes oficiais da tag v3.1.0 conferidas no commit `0aaef29d04bec9567bdf2f669036fabecc33a2e2`; cópia de consulta sob artifacts, sem executar/compilar fontes de terceiros. As chamadas usadas pelo probe e pelos descritores compilam contra os assemblies NuGet existentes. Não houve pacote, reflection, API privada ou binário adicional.

| Necessidade | API pública conferida / evidência | Decisão |
| --- | --- | --- |
| Endpoint e cliente explícitos | MMDeviceEnumerator.GetDevice, MMDevice.DataFlow/State/CreateAudioClient | ID/direção/geração obrigatórios; nenhum fallback |
| Mix e suporte exato | AudioClient.MixFormat; IsFormatSupported(Shared, WaveFormat, out WaveFormat) | Consulta antes de inicializar; true/S_OK e closest nulo para capacidade exata. Closest nunca aplicado |
| Períodos | AudioClient.DefaultDevicePeriod/MinimumDevicePeriod, long | Ambos em 100 ns; Minimum é período **exclusivo**, não mínimo shared |
| Buffer e latência | AudioClient.BufferSize/StreamLatency | Exigem cliente inicializado; campos null, sem inventar valor |
| Formato extensível | WaveFormatExtensible.SubFormat/ValidBitsPerSample/ChannelMask e construtor público completo | Preservar dados nativos; container 32 não implica float |
| Captura | AudioCaptureClient.GetBuffer/GetBufferLease/ReleaseBuffer; WasapiRecorder.DataAvailable | Buffer emprestado, flags e posições disponíveis; não invocados nesta tarefa |
| Reprodução | AudioRenderClient.GetBuffer/GetBufferLease/ReleaseBuffer | Acquire/release na mesma thread e dentro do prazo; nenhum buffer nativo solicitado |
| Clock | AudioClockClient.Frequency/GetPosition(out ulong, out ulong) | Há posição e QPC correlacionados, mas há lacuna na evidência de precisão; não usar nativamente neste incremento |

Referências imutáveis NAudio: [AudioClient](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/CoreAudioApi/AudioClient.cs), [WaveFormatExtensible](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Core/Wave/WaveFormats/WaveFormatExtensible.cs), [AudioCaptureClient](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/CoreAudioApi/AudioCaptureClient.cs), [AudioRenderClient](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/CoreAudioApi/AudioRenderClient.cs), [WasapiRecorder](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/WasapiRecorder.cs), [WasapiPlayer](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/WasapiPlayer.cs).

### Lacuna que bloqueia clock/stream nativo

Na fonte fixada, AudioClockClient.GetPosition testa HRESULT **−1** para retornar false; depois chama ThrowIfFailed e retorna true. Microsoft documenta **S_FALSE (+1)** para posição obtida com precisão inferior. Logo o bool desse wrapper não basta para distinguir S_OK de S_FALSE. AdjustedPosition extrapola posição com tempo local; WasapiPlayer.GetPosition não preserva toda a evidência requerida. Não usar esses atalhos como clock físico preciso. Evidência: [wrapper exato](https://github.com/naudio/NAudio/blob/0aaef29d04bec9567bdf2f669036fabecc33a2e2/src/NAudio.Wasapi/CoreAudioApi/AudioClockClient.cs) e [contrato GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition).

Opções futuras, após revisão: correção pública verificada do wrapper ou interop COM mínimo com a interface pública IAudioClock, preservando HRESULT e ownership, testado em etapa própria. Nenhuma dessas opções foi implementada aqui. Não acessar campos privados nem afirmar que a negociação efetiva está confirmada pelo probe.

## Descritor e consulta real

WasapiEndpointProbe.QueryAsync executa em MTA; MMDeviceEnumerator, MMDevice e AudioClient são locais e descartados nessa operação. Retorna apenas modelos gerenciados, incluindo ID completo, direção, geração, estado, tag, subformato, sample rate, canais, bits de container/válidos, block align, bytes/s, cbSize, máscara e períodos. Cancelamento observado antes/depois das consultas; uma chamada COM já iniciada não é atomicamente cancelável. Erro/indisponibilidade não seleciona outro endpoint.

Subconjunto validado: PCM16 LE ou IEEE float32 LE, 44,1/48 kHz, mono ou LR. EXTENSIBLE exige cbSize ≥22, GUID coerente e bits válidos/container exatos. Estéreo extensível exige máscara **3 = FL, FR**, na ordem dos bits; WAVEFORMATEX estéreo usa a convenção L/R. Mono é um único sinal, com máscara zero (posição não especificada) ou uma posição declarada conhecida; não se inventa posição física. Bits reservados, máscara estéreo ambígua, multicanal, packed24, PCM32 e formatos inconsistentes são recusados. Rotas adaptativas aceitam somente 48→48 e 44,1↔48; não há fallback, remix implícito ou flags de conversão automática neste caminho.

MixOnly, ExactCapability e SimulatedExact distinguem evidências. StreamInitialized permanece false na consulta. IsFormatSupported confirma capacidade do mix compartilhado, **não** abertura, formato físico ou negociação efetiva. Futuro cliente deve inicializar explicitamente o formato escolhido, sem AutoConvertPcm/SrcDefaultQuality, verificar resultado e registrar formato, flags, buffer e latência reais. O diagnóstico curto Alpha já existente tem política própria de conversão Windows e permanece intacto. [GetMixFormat](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getmixformat), [IsFormatSupported](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-isformatsupported), [WAVEFORMATEXTENSIBLE](https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible), [GetDevicePeriod](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getdeviceperiod).

Consulta real neste ambiente: captura mono float32, 48 kHz, 32 bits válidos/container, máscara 4, align 4, 192.000 bytes/s; reprodução LR float32, 48 kHz, máscara 3, align 8, 384.000 bytes/s. Ambos EXTENSIBLE/cbSize22, capacidade exata, período padrão 100.000 ×100 ns =10 ms e mínimo exclusivo 30.000 ×100 ns =3 ms. Buffer/latência efetivos desconhecidos/null. Não se iniciou áudio. Relatório local numeric-only em artifacts/wasapi-backend/native-probe.json, sem IDs/nomes no relatório.

## Unidades e qualidade do clock

WasapiClockAdapter é serial por papel/geração. Entrada NativeClockSample exige identidade, position/frequency, unidade Frames ou DeviceUnits, QPC em 100 ns, qualidade explícita e flags. Capture GetBuffer documenta device position em frames; render exige divisão pela frequência de IAudioClock, que pode ter outra unidade. Frequência nominal sozinha não mede drift. QPC correlacionado de ambas as APIs já vem em 100 ns: não somar ticks crus de Stopwatch nem bytes processados. [Captura](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), [frequência](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency).

Primeira posição ancora frame zero; deltas unsigned × sampleRate em UInt128, divisão inteira e resto acumulado evitam converter contador absoluto para double. FramePosition/QPC comum são long com overflow validado. Teste usa posição **e QPC acima de 2⁵³**. Identidade do AudioClock é hash limitado do ID/direção, geração separada; o adapter confere a identidade nativa completa antes da conversão.

Somente Accurate produz observação Monotonic; isso é evidência fornecida explicitamente pelo fake, não precisão nativa inferida. Unavailable/Uncertain, timestamp error, flags desconhecidas, descontinuidade, regressão, salto, QPC futuro/idade >500 ms, frequência/unidade trocada ou overflow invalidam a entrega e exigem nova sessão. Falha fica retida; outra identidade não altera baseline. Silent permite payload vazio, produz zeros e preserva clock válido; não é timestamp error. Sessão reage imediatamente a qualquer retorno inválido, inclusive Discontinuity do estimador, sem esperar expirar Latest.

## Sessão, propriedade e segurança

IsolatedTransportSession possui worker MTA dedicado, fila de controle limitada a oito comandos e estados Closed/Preparing/Priming/Running/Stopping/Faulted. Start explícito recebe rota/geração nova; somente ISimulatedAudioTransport existe. Não há adaptador nativo de Start que finja sucesso. Backend, recursos parciais, stop e dispose pertencem ao worker. Produtor e consumidor têm threads distintas e estáveis, CAS contra reentrada, spans emprestados e scratch pré-alocado. Identidade de cada pacote é validada. Não há COM, disco, rede, espera ou alocação recorrente no processamento válido.

Produtor: decodifica PCM16/float32 ou silêncio, aplica comando de ppm da geração corrente no próprio produtor, processa AdaptiveResampler e escreve o prefixo aceito na SPSC. Consumidor: lê a SPSC, aplica somente mute/atenuação digital e codifica PCM16/float32; zeros de priming/underrun são explícitos. Default: **muted=true, nível digital 0,2**. Desmutar é comando explícito da simulação; nenhum nível Windows é escrito. Não há acesso destes comandos na UI.

Stop/cancel/falha invalida imediatamente DeliveryTicket, antes de esperar worker/callback. A invalidação é definitiva; Enable usa CAS e não reativa ticket já cancelado. Submit fake confere o ticket no ponto final de entrega; geração e IDs são imutáveis. Recursos/adaptativo somente param/resetam/descartam após ambos os papéis quiescentes. Contabilização em voo pode terminar após Stop; isso não autoriza entrega. Não é possível revogar áudio já submetido. [Contrato do render](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiorenderclient-getbuffer), [release](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiorenderclient-releasebuffer).

Preparação parcial, falha, cancelamento durante abertura, backend desconectado/suspenso e comandos/packets atrasados são exercitados. Falhas ficam retidas; recuperação manual exige geração maior, adaptativo/estimador/controlador novos, priming e descarte da fila antiga contabilizados. Nenhum fallback/restart automático. Saturação de fila de comandos rejeita pedido, mas Stop já invalida a geração; worker também observa essa invalidação. Valores não finitos/faixa de ganho são rejeitados.

Espera de quiescência do controle limitada a 1 s; se expirar, sessão Faulted mantém recursos e interlock, sem destruir memória em uso. DisposeAsync tem esperas de até 3 s por comando/encerramento; se callbacks persistirem, informa falha e permite repetir descarte após o retorno. Não existe garantia de abortar uma chamada nativa que bloqueou; interface aqui é fake. Recursos não são destruídos à força. Backend simulado libera render antes de capture, também após início parcial, na thread MTA proprietária. Testes de barreira só existem no fake de teste.

TransportActivityGuard tem leases mutuamente exclusivos para diagnóstico curto e transporte. Está testado isoladamente; **a Alpha não foi conectada** a esse guard nem à sessão nova. Integração futura deve injetar a mesma instância no diagnóstico e no motor antes de permitir qualquer captura dupla. Monitoramento físico seguirá opt-in, headset confirmado e nível digital baixo, separado de endpoint/session volume.

## Orçamento, média temporal e evidências

Períodos são convertidos por teto para **frames de saída**. Orçamento isolado aceita período até 512 frames; alvo potência de dois ≥max(512, 2×período), capacidade 2×alvo. Nos endpoints medidos: período480, alvo/priming1024 e capacidade2048. FIFO-alvo 21,333 ms e capacidade42,667 ms a 48 kHz; FIR adiciona 80 frames de entrada (1,667 ms a48/1,814 msa44,1). São parcelas teóricas, não latência total/round-trip nem buffer físico consultado. Sem promessa de baixa latência física.

Priming escreve zeros reais antes de start fake. Contadores separam priming, produzido/aceito, lido, drops, underruns, submetido, mute e fila descartada. Histórico/cauda FIR ainda não materializados não viram frames fictícios da fila. Proteções do adaptativo e pipeline continuam em seus próprios estágios; não somá-las como amostras únicas. A regressão de qualidade/proteção da F2-I4 permanece.

Cada papel integra contagem cumulativa de frames × tempo do evento (UInt128), publicando ledger por seqlock. Worker combina área do priming + área do produtor − área do consumidor e divide o delta de área pelo intervalo. Não é média de snapshots esparsos. Teste com mesma contagem final e eventos em tempos diferentes distingue áreas; fila1024, +480 em .01 s, −480 em .02 s dá **média 1184** até .03 s.

**Limite deliberado do fake:** Tick exige watermark ClosedThrough100ns que atesta término de todos os eventos até o corte e proíbe callback posterior nesse intervalo. Sem watermark rejeita tick; ledger já avançado para evento futuro ou inconsistente causa falha, sem inventar passado. Para backend nativo concorrente será necessário checkpoint/eventos fechados verificáveis; este protocolo sintético não prova que o Windows fornece watermark. Callbacks tardios até o corte são recusados. Média arredondada passa ao controlador interior [1,cap−1]; contadores reais de drop/underrun sinalizam fronteira cap/0 e ReprimeRequired invalida entrega. Intervalo de controle válido até500 ms; clockestimator espera janela ≥1 s antes de Tracking, limite ±500 ppm, slew25 ppm/s.

Prova FIFO: 48.000 frames produzidos/lidos, priming1.024 e descarte final1.024, zero drop/underrun. Hash de bytes submetidos **1A1C0D8E5FE4E9FD**, igual à referência FIR + priming + atenuação, incluindo sinal senoidal variável e mono→LR. Outros quatro caminhos exercitam PCM16↔float32 e 48→48/44,1↔48 com blocos irregulares. O degrau tem ringing FIR previsto; teste verifica ganho estabilizado0,1 e pico limitado0,12, separadamente. Não é teste auditivo.

Ensaio adicional de 5 s, captura com clock+300 ppm e playback nominal, com pacotes de480/481 frames: alvo−100 ppm e CurrentPpm aplicado pelo produtor−97,5 ppm no final,240.063 frames produzidos e 240.000 lidos, zero drops/underruns. O snapshot separa alvo/aplicado; a razão efetiva de amostras muda de verdade. É início de slew, sem alegar convergência em 5 s ou estabilidade física; os ensaios longos anteriores permanecem em regressão.

Relatórios locais em artifacts/wasapi-backend: native-probe, callbacks, fifo-hash, occupancy, applied-drift, samples, overrun/underrun e quiescence. Sem conteúdo de áudio salvo, dados pessoais ou IDs. Métricas finais de build/testes/custo e republicação estão no PROJECT_STATUS e TEST_PLAN. Testes anteriores são preservados; não atribuir a esta etapa resolução da causa histórica de intermitência do runner.

## Condições para uma eventual F2-I6

Parar para revisão. Integração física continua bloqueada até aceitação dos casos Alpha relevantes A01–A12, revisão deste contrato, solução auditada do HRESULT de clock, negociação/ownership reais e autorização específica. Planejar start opt-in, interlock diagnóstico/monitoramento compartilhado, headset em nível baixo, sem modificar níveis Windows, stop imediato, medição separada de latência e recuperação manual por geração. Validar Windows 11 x64 separadamente; proprietário dispõe somente de Windows 10. Não iniciar Fase 3, efeitos em massa, EQ, driver, instalador, redesign ou release pública nesta entrega.
