# Backend WASAPI — transporte isolado e preparação da prova audível

## F2-I6D-AUDIBLE-PROOF — aprovação manual e evidência disponível

Em 19/11/2026, o proprietário confirmou que ouviu a própria voz pelo headset e que o monitoramento funcionou. A aprovação manual cobre a função de escuta. O relato disponível não detalha nível durante a reprodução, timbre, ruído ou distorção, atraso percebido, feedback, desconforto ou acionamento de Esc. O procedimento do teste prevê até cinco segundos e ganho máximo de 0,05. A duração e o ganho efetivos da escuta relatada permanecem desconhecidos.

| Evidência recuperada | Resultado |
| --- | --- |
| Base e binários | `fa0e5f2` equivale funcionalmente a `0c1e54b`. Os hashes SHA-256 do apphost e das DLLs de testes e áudio conferem com o build de `e14ff84` |
| Preparação | Restore locked com auditoria de todos os pacotes e NU1900 como erro. Build não incremental Release x64 com zero erros e avisos. Descoberta por filtro exato de um único teste `Explicit` |
| Janela acompanhada | Aberta em 18/11 às 23:59, horário local, após as confirmações de preflight do proprietário e verificação de ausência de Wave Studio e de outro runner. Último estado `AguardandoOperador`, anterior ao lançamento do apphost |
| Evidência da execução relatada | Busca em `artifacts`, nas demais pastas do Wave Studio e nas pastas locais de documentos, área de trabalho, downloads e temporários acessíveis. Nenhum JSON da prova, log de execução ou código de saída foi recuperado. Os logs de regressão registram a exclusão do método `Explicit` |

O estado local preservado está em `artifacts/wasapi-audible-proof/e14ff84-audible-1795057154072-state.json`, com SHA-256 `6666753808496b25f4ba8f9b036abdb33d5e3b389df109ff21dde5650d519487`. Os três logs de preparação `e14ff84-restore.log`, `e14ff84-build.log` e `e14ff84-discovery.log` estão na mesma pasta, ignorada pelo Git. Os comandos de preparação estão no registro anterior. [Preflight documentado em 95f4ffd](https://github.com/Wave-Universal-Studio/main/blob/95f4ffd31a3e633c90c77fe81d35b588fb72adef/docs/WASAPI_BACKEND_PLAN.md#f2-i6d-audible-proof--preflight-e-resultado-pendente).

Para a escuta relatada permanecem sem verificação o runner específico, formato negociado, janela QPC, flags de conclusão e cadência nominal, frames SILENT e PCM, pico e clipping, cortes e estado da FIFO, e a confirmação de Stop e Release. A ausência desses arquivos deixa os critérios automatizados indisponíveis. O relato de escuta permanece registrado como evidência manual. O código e as provas anteriores foram preservados. A validação de monitoramento contínuo, drift físico e latência acústica segue pendente de decisão de planejamento. [Status e decisão para a Fase 3](../PROJECT_STATUS.md#decisão-para-iniciar-a-fase-3).

## F2-I6D-ALLOC-MONITOR — roteiro para revisão

A fachada interna `WasapiAudibleProofSession` reutiliza os proprietários WASAPI, a política nominal, o FIR, a FIFO e o codificador já exercitados no backend simulado. As fachadas nativas B1, B2 e nominal SILENT continuam sem codificador. O novo caminho nativo exige capacidade exata nos dois endpoints e consentimento de uso único associado à rota e ao token de parada. A interface e o monitoramento contínuo ficam fora desta etapa.

O ganho parte de zero. Um corte coerente após o preenchimento da FIFO, com consentimento vigente, permite a rampa de até 5 ms rumo ao teto fixo de 0,05 (cerca de −26 dB de ganho digital em amplitude). Continuam valendo prazo de observação de até cinco segundos, limites de fila, relógios, contabilidade, conversões Float32/PCM16 e política de falhar com segurança. A eventual diferença de até meio passo do PCM16 é arredondamento da amostra submetida. O recorte de saída do FIR é contabilizado antes do descarte do caminho de áudio. O projeto não altera volume nem dispositivo padrão do Windows.

### Preparação da tentativa futura

1. Após revisão e nova autorização para captura temporária **e reprodução audível**, confirmar headset físico colocado, saída sem caixas abertas, volume baixo e parada ao alcance do proprietário. Confirmar a entrada e a saída exatas, Wave Studio fechado normalmente, ausência de chamadas, gravações ou outros runners e os sete manuais já aprovados. A seleção é única por direção, entre dispositivos ativos com formato exato. O teste confere processos do Wave Studio e outros apphosts antes de habilitar a tentativa.
2. Usar o apphost Release x64 em console interativo de primeiro plano. Opt-ins exclusivos do processo: `WAVESTUDIO_AUDIBLE_PROOF_START_AUTHORIZED`, `WAVESTUDIO_AUDIBLE_PROOF_PREFLIGHT_CONFIRMED`, `WAVESTUDIO_AUDIBLE_PROOF_HEADSET_CONFIRMED` e `WAVESTUDIO_AUDIBLE_PROOF_LOW_LEVEL_CONFIRMED` recebem `1` somente após as respectivas confirmações. Informar os nomes exatos em `WAVESTUDIO_AUDIBLE_PROOF_CAPTURE_NAME` e `WAVESTUDIO_AUDIBLE_PROOF_HEADSET_NAME`. O filtro exato é `WaveStudio.Tests.WasapiNativeAudibleProofTests.AuthorizedHeadsetRunsBoundedAudibleNominalProof`, com `--explicit only --filter-method <método> --timeout 45s`. A prova anterior em SILENT tem autorização própria já consumida.
3. Testar Esc antes de confirmar a tentativa com Enter. Um segundo Esc antes de Enter cancela a tentativa. A confirmação expira em 15 s. Durante a sessão, Esc ou o cancelamento cooperativo do runner invalida a entrega. Ao perceber desconforto, nível imprevisto ou feedback, usar Esc imediatamente. A perda do acesso às teclas dispara cancelamento. O guard continua reservado até os donos encerrarem e confirmarem liberação. Antes do Release, a entrega revalida a validade do ticket e o prazo e sinaliza SILENT se a entrega foi invalidada. PCM já entregue ao Windows pode completar a reprodução.
4. O JSON local em `artifacts/wasapi-audible-proof` contém formatos e números, sem nomes, IDs ou PCM. Conferir frames produzidos/lidos, priming, warmup, margem, cortes, prazo, falhas, Stop e Release. Os contadores separam frames SILENT de PCM liberado com S_OK, frames com sinal diferente de zero, ganho digital de pico, pico quantizado e clipping. A aceitação futura exige transporte nominal e PCM não nulo confirmado, além do relato do proprietário sobre som, nível, timbre e ausência de feedback. Margem de fila e latência algorítmica são métricas separadas da latência acústica.

Nesta etapa houve descoberta do método e exercícios com fakes. A coleta física nominal SILENT anterior segue registrada abaixo.

## F2-I6C-NOMINAL-PROOF — resultado físico, 18/11/2026

A tentativa única autorizada foi **aprovada no escopo de transporte nominal limitado**. O proprietário reconfirmou os endpoints físicos, os sete manuais e a ausência de chamadas, gravações e outros testes. O Windows confirmou um endpoint ativo por direção para o par exato e não havia Wave Studio ou outros runners. O teste validou o mix e suporte exatos antes de Start, sem fallback.

O Release x64 foi recompilado de `134f35e70c602092802220d35d3953a3828416ec`, com código idêntico a `287b05d4f340bc3dcd91ef18be53fc678b09932c`. Apphost e DLLs de áudio e testes registraram essa revisão. Restore com `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900` e build com `dotnet build WaveStudio.slnx -c Release --no-restore --no-incremental`, com zero erros ou avisos. A descoberta `--list-tests text --filter-method` encontrou somente `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsRunBoundedSilentNominalTransport`. A execução usou apphost direto com o mesmo filtro e `--explicit only --timeout 30s --diagnostic --diagnostic-output-directory`. Os opt-ins ficaram no processo de execução. A política Restricted foi preservada.

| Critério | Resultado |
| --- | --- |
| Runner | 1 teste aprovado, código 0, duração de 5,998 s no runner e 6,361 s na medição do processo |
| Tempo e aceitação | `DurationReached=true`, `NominalObserved=true`. Janela QPC de 49.991.228 × 100 ns, ou 4,9991228 s, 0,8772 ms abaixo dos 5 s programados |
| Evidência e modo | `PacketRateConfirmed=true`, `RenderSilent=true`, `DriftPrecisionAvailable=false`, `DriftControlEnabled=false`, `Cleanup.TransportObserved=false` |
| Encerramento | `Completed=true`, ambos os streams liberados, sem buffers em voo, erro de limpeza ou recursos retidos. Nenhuma voz armazenada |

### Frames e FIFO

| Evidência | Captura | Render SILENT |
| --- | --- | --- |
| Mix exato | Float32, 48 kHz, mono, máscara 4, 4 bytes/frame | Float32, 48 kHz, LR, máscara 3, 8 bytes/frame |
| Capacidade e período | 1.056 frames, 10 ms | 1.056 frames, 10 ms |
| Initialize / Start / último GetBuffer / último ReleaseBuffer / Stop | Confirmado / S_OK / S_OK / S_OK / S_OK | Confirmado / S_OK / S_OK / S_OK / S_OK |
| Aquisições / liberações | 500 / 500 | 501 / 501 |
| Frames adquiridos / liberados | 240.000 / 240.000 | 241.056 / 241.056 |
| Captura aceita e descarte | 499 pacotes, 239.520 frames aceitos. 1 pacote, 480 frames com `DATA_DISCONTINUITY` descartados integralmente no startup | — |
| FIFO | 239.520 frames produzidos, zero drops | 237.120 frames lidos, zero underruns |

A contabilidade de render fecha em 241.056 = 1.056 priming do endpoint + 0 pré-ticket + 2.880 warmup + 237.120 tentativas FIFO atendidas. O preenchimento da FIFO foi de 2.400 frames e constitui uma contagem distinta do priming nativo e das liberações durante warmup. O saldo terminal escrito menos lido foi de 2.400 frames. As tentativas de leitura coincidiram com os frames lidos. Não houve parada por fronteira.

Com FIFO de 4.096 frames, a margem calculada foi `max(1056, ceil(1056 × 48000 / 48000) + 1) = 1057`. A captura protege o limite alto de 3.039 frames antes de adquirir novo pacote. Após o warmup, o render protege o limite baixo de 1.057 antes de ler a FIFO. Os 191 cortes coerentes tiveram ocupação de 0 a 2.400 frames. Como a faixa agrega a sessão inteira, ela não localiza o mínimo em relação ao warmup nem representa cada fronteira de publicação.

Houve 26 consultas ocupadas e 49 observações independentes em cada papel. Os limites ponderados da média foram 2.091,054482–2.109,499372 frames em 4,9724887 s cobertos (99,467% da janela QPC observada). O intervalo coberto ficou 26,6341 ms abaixo da janela total. O relatório agrega cortes e áreas, sem série por corte para recalcular a distribuição de intervalos ou o maior intervalo entre cortes. O sucesso confirma que o controle não registrou vencimento da regra de 500 ms e que o último corte cumpriu o critério de frescor. A ocupação não mede latência acústica.

### Movimento e evidência temporal

O histórico tem 49 leituras sequenciais em cada papel, sem sobrescrita. A captura reportou 29 válidas, 19 provisórias e uma `NominalEvidenceUnavailable`, com 28 avanços válidos contabilizados. O render reportou 49 válidas e 48 avanços válidos. A frequência foi constante, 192.000 unidades/s na captura e 384.000 no render, e os HRESULTs de frequência e posição foram S_OK. As posições não regrediram, os QPCs foram crescentes e as idades observadas foram 0,7161–19,3268 ms na captura e 0,0002–0,1044 ms no render. O maior erro curto registrado foi de 481,536 frames na captura e 2,1792 no render. O histórico não contém rejeição fatal de integridade.

A leitura 27 da captura, com posição bruta de 524.160 unidades e QPC 58.817.527.646 × 100 ns, registrou `NominalEvidenceUnavailable/WindowRateOutsideTolerance`. A janela móvel avançou 97.440 frames em 2,0194117 s, para 96.931,7616 esperados. O resíduo de +508,2384 superou o limite de 486,658808 em 21,579592 frames. O acumulado daquela leitura permaneceu dentro do limite, com resíduo de +508,488 ante 630,65756. O modo manteve o transporte sob a regra de evidência independente dos pacotes. O relatório conserva o motivo e a janela rejeitada, porém não guarda a janela de pacotes individual no instante 27 para recálculo isolado.

A leitura 28 voltou a `Valid`. Seu resíduo móvel foi de +508,2624 frames ante limite de 510,658688. A regra contabiliza 99,9995 ms entre os QPCs 27 e 28. Entre as observações locais decorreram 100,8234 ms. Do QPC rejeitado até a observação local da recuperação decorreram 116,1264 ms, também abaixo de 500 ms. Essa foi a única indisponibilidade registrada e não habilitou correção.

| Série terminal | Duração | Frames avançados | Frames esperados | Resíduo em frames | Limite em frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| Clock de captura, móvel | 2,1199892 s | 101.760 | 101.759,4816 | +0,5184 | 510,797408 |
| Clock de captura, acumulada | 4,8393952 s | 232.800 | 232.290,9696 | +509,0304 | 1.163,454848 |
| Pacotes, móvel | 2,0194499 s | 96.960 | 96.933,5952 | +26,4048 | 486,667976 |
| Pacotes, acumulada | 4,9800017 s | 239.040 | 239.040,0816 | −0,0816 | 1.197,200408 |

Houve 28 janelas confirmadas do clock e 299 dos pacotes. Os 239.040 frames entre o início do primeiro e do último pacote aceito, somados aos 480 do último pacote, fecham o total de 239.520 aceitos. `ContinuousPackets=499` coincide com `PacketsAccepted`. A corroboração terminal dos acumulados foi recalculada na sobreposição de 4,8393952 s como 509,109696 frames, ante limite de 1.163,454848, com `CaptureTiming.Confirmed=true`. Essa consistência nominal e a recuperação da janela não identificam causa física nem fornecem uma estimativa calibrada de drift.

### Preservação e pendências

O JSON original fica em `artifacts/wasapi-nominal-silent`, com SHA-256 `B996E9C632B74344DB4244D5EA44321BDA481583974511BB2A15E8A1F8BCC658`. O log bruto é `runner-134f35e.log` nessa pasta, ignorada pelo Git. Foram publicados somente números e conclusões, sem nomes ou IDs de endpoints, voz ou PCM.

Os logs anteriores de alocação em `artifacts/f2-i6c-nominal` mostram 7.912 bytes no Release padrão com seed 20261113 e 6.856 com seed 20261114 no teste do diário. A asserção mede a thread do teste após aquecimento, com exigência de zero bytes na publicação medida. O Release serial com seed 20261113 aprovou os 801 casos comuns, mesmos binários e asserções. A diferença de contexto não estabelece a origem dos bytes. Nenhuma suíte foi repetida nesta prova física.

A prova 4 e a coleta diagnóstica reprovada foram preservadas. A aprovação atual fica restrita ao transporte nominal curto no par e formato testados. Monitoramento audível ou contínuo exige resolver a pendência de alocações e obter revisão e autorização próprias. Controle de drift físico, estabilidade prolongada e F3 permanecem pendentes.

## F2-I6C-NOMINAL-MONITOR — transporte limitado em razão fixa

`WasapiNominalSession` é uma fachada separada que usa o mesmo ciclo de vida WASAPI, guard, owners MTA, conversão e FIFO de B1/B2. A fachada `WasapiSilentSession` mantém os seus critérios anteriores. O modo nominal tem relatório e critério `NominalObserved` próprios. O relatório nominal sempre declara a precisão de drift indisponível. O modo não constrói o controlador adaptativo, não publica ppm e mantém a razão em zero ppm de correção.

### Replay e suspensão limitada da evidência de clock

O JSON original da coleta `59cc56e` foi conferido com SHA-256 `E236D02F37364D1C729EC3A58CA04693EF16EB3B74E02584306EBB9843801C26`. O replay retém as 37 leituras e o QPC local do clock de captura, com origem comum subtraída. Reproduz a leitura 37 como `InvalidPosition/WindowRateOutsideTolerance` no contrato anterior, com resíduo de −507,1056 frames e limite de 486,935528. O arquivo retém contagens e janelas de pacotes, sem série bruta completa por pacote. Por isso, o replay positivo da nova regra combina o clock real com uma série nominal **sintética** de pacotes inteiros, contíguos e frescos. A mesma leitura falha no novo modo quando os pacotes estão ausentes ou obsoletos. A prova histórica permanece reprovada.

A única exceção no modo nominal é uma janela móvel da captura fora do limite nominal com os demais critérios atendidos. Exige janela de clock confirmada anteriormente, acumulado do clock dentro do limite nominal e janelas móvel e acumulada dos pacotes frescos e dentro dos limites. A leitura fica registrada como `NominalEvidenceUnavailable`, com motivo, números brutos e âncoras. O adaptador continua verificando a integridade das novas leituras, sem converter a evidência rejeitada em clock válido e sem reutilizar um adaptador marcado `RequiresReset`. O tempo acumulado de indisponibilidade deve permanecer abaixo de 500 ms por sessão. A evidência independente de pacotes precisa continuar disponível durante esse intervalo. Uma repetição sustentada encerra a sessão. Nenhuma tolerância existente foi ampliada.

O clock de render mantém a validação estrita. HRESULT incerto, regressão, ausência de progresso, mudança de frequência/unidade/identidade, QPC inválido ou velho, flags inválidas, falha dos buffers e descontinuidade depois do primeiro pacote continuam fatais. O timestamp do pacote refere-se ao primeiro frame de captura e só confirma a contabilidade após liberação integral. O QPC do clock identifica a posição lida na frequência do stream. O QPC local mede quando a operação foi observada ou publicada. São séries com semânticas distintas. [GetBuffer](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), [GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition), [GetFrequency](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency).

### Prazo, ocupação e amostras

A sessão aceita no máximo 5 s configurados. Ao vencer o prazo, os owners deixam de adquirir novos buffers; um buffer em voo ainda recebe a liberação contratual. A fiscalização em hardware no modo de prova usa consultas no controle espaçadas por 23 ms, com as tentativas limitadas já existentes na leitura do diário. A leitura individual de cada publicação por versão mantém evidência de progresso mesmo se o outro owner estiver ocupado. A ocupação e a média usam somente cortes coerentes e intervalos completos B2A. O controle para após uma lacuna de 500 ms sem corte ou perda do histórico. A lacuna de 1,651 s da prova anterior não satisfaria essa regra.

A margem é `max(capacidadeRender, teto(capacidadeCaptura × taxaRender / taxaCaptura) + 1)` em frames de saída. A FIFO mantém sua capacidade original; a rota é recusada antes de Start se a margem ocupar metade dela. A captura não inicia novo pacote quando a ocupação conservadora atinge capacidade menos margem. Depois do preenchimento inicial, o render só faz uma tentativa de leitura acima da margem. Assim, um bloco inteiro cabe antes de cada fronteira. As leituras conservadoras por papel são usadas apenas para parar. A aceitação usa os cortes do diário. Perda confirmada encerra a entrega e o resultado exige zero drops e zero underruns. O preenchimento da FIFO continua separado do priming do endpoint e das liberações antes do ticket e durante warmup.

No exemplo histórico de 48 kHz, capacidade nativa de 1.056 e FIFO de 4.096 frames, a margem é 1.057 frames e o limiar de produção é 3.039. Um desbalanceamento relativo hipotético de 300 ppm acumula 72 frames em 5 s e um período de 10 ms representa 480 frames. A folga depende da ocupação depois do preenchimento e do agendamento; as bordas e o prazo limitam a operação real. A janela nominal admite desvios grandes demais para garantir operação indefinida. A demonstração em fakes não calibra drift ou jitter físico.

A prova nativa nominal sempre libera com SILENT. A escrita de amostras existe exclusivamente na fábrica com rota e backend simulados. O produtor passa pelo FIR em razão nominal e pela mesma FIFO. O render codifica em Float32 ou PCM16, conforme o formato negociado, com ganho digital inicial zero, teto 0,1 (−20 dB de amplitude) e rampa de até 5 ms para o curso completo. NaN e infinito viram zero e a amplitude fica limitada. Se o ticket for revogado antes da submissão, o buffer simulado é zerado e liberado como SILENT. A liberação já entregue ao backend não pode ser revogada. B1/B2 preservam a entrega SILENT sem escrita PCM em seus buffers. Falha em Release ou Stop segue a retenção existente do guard até quiescência confirmada.

### Próximas provas, sujeitas a revisão e autorização

A prova SILENT nominal preparada é `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsRunBoundedSilentNominalTransport`, marcada `Explicit`. A descoberta pode usar `--list-tests text --filter-method` pelo nome qualificado. Em uma execução autorizada, verificar os sete manuais aplicáveis, conectar os dispositivos, confirmar Wave Studio fechado, ausência de chamadas, gravações e outros runners, selecionar um endpoint ativo e exato por direção, conferir proveniência Release x64 e usar o filtro único com `--explicit only`. Os opt-ins de processo são `WAVESTUDIO_NOMINAL_SILENT_START_AUTHORIZED`, `WAVESTUDIO_NOMINAL_SILENT_PREFLIGHT_CONFIRMED`, `WAVESTUDIO_NOMINAL_SILENT_CAPTURE_NAME` e `WAVESTUDIO_NOMINAL_SILENT_HEADSET_NAME`.

A aceitação exige duração configurada concluída, movimento em ambos os papéis, evidência nominal de pacotes, cortes coerentes dentro do prazo, contabilidade sem perdas, margem respeitada, Stop e Release confirmados, sem buffers ou guard retidos. O relatório fica em `artifacts/wasapi-nominal-silent`, com números, formatos e motivos, sem nomes, IDs ou voz. `NominalObserved` não aprova B1, a coleta diagnóstica anterior nem a precisão de drift. Em caso de falha, conservar evidência e encerrar sem retry automático.

Uma prova audível posterior depende de aprovação nominal, revisão específica do código e resolução adequada da pendência de alocações. Exigiria autorização distinta, headset e volume baixo, ganho digital começando em zero e limitado a −20 dB de amplitude, duração de até 5 s, parada imediata por solicitação ou falha de integridade e verificação de timbre/nível e encerramento. Medidas de atraso em frames ou QPC da cadeia permanecem distintas de latência acústica. Nenhuma escrita nativa audível foi habilitada nesta etapa.

## F2-I6B2B-DIAG-PROOF — resultado físico, 18/11/2026

A tentativa física única foi executada sob autorização expressa e confirmação imediata do proprietário, com os sete casos manuais ainda aplicáveis, dispositivo USB de saída e dispositivo de captura corretos conectados, Wave Studio fechado e ausência de chamadas, gravações, testes e outros runners. A consulta Windows confirmou um único endpoint ativo por direção, e o runner confirmou capacidade exata e o mix antes de Start, sem fallback. O Release x64 foi recompilado de `bcafcda`, com código funcional idêntico a `558a43165f391fdd66153d36304206b568a21479`. O build terminou com zero avisos e erros e o apphost e as DLLs de áudio e testes identificavam a revisão `bcafcda`.

Restore com `dotnet restore WaveStudio.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900`. Build com `dotnet build WaveStudio.slnx -c Release --no-restore --no-incremental`. Descoberta pelo apphost com `--list-tests text --filter-method` e o nome qualificado `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedEndpointsCollectSilentDriftEvidenceAtNominalRatio`. A tentativa usou o mesmo filtro com `--explicit only --timeout 30s --diagnostic --diagnostic-output-directory`. Os quatro opt-ins foram definidos apenas no processo de execução; a política Restricted foi preservada. A primeira verificação automatizada do rótulo de arquitetura precisou considerar o formato `net10.0|x64` antes do Start. Isso afetou apenas a descoberta; houve uma execução de teste físico.

### Resultado e encerramento

| Critério | Evidência da tentativa |
| --- | --- |
| Runner | 1 teste executado, 1 falha, código 2. Duração de 5,060 s no runner e 5,349 s no processo observado. |
| Observação | 37.220.736 × 100 ns, ou 3,7220736 s. Temporizador de 10 s interrompido por rejeição na captura. |
| Aceitação | `DurationReached=false`, `TransportObserved=false`, `CaptureTiming.Confirmed=false`. Coleta não aprovada. |
| Cortes e janelas | 34 consultas, 20 cortes coerentes de 60 exigidos, zero janelas estimáveis de 10 exigidas. O requisito de pelo menos 4 s entre janelas não pôde ser atendido. |
| Razão nominal | `CorrectionEnabled=false`, `DriftControlEnabled=false`, `TrackingUpdates=0`. `RequestedPpm=null` e `AppliedPpm=0` nas 34 amostras. |
| Segurança e limpeza | Render sempre SILENT, sem PCM persistido. Start, último Get/ReleaseBuffer e Stop com S_OK em ambos os papéis. `Completed=true` indica encerramento confirmado. Ambos liberados, sem buffers em voo, erros de limpeza ou recursos retidos. |

| Contabilidade | Captura | Render SILENT |
| --- | --- | --- |
| Mix exato | Float32, 48 kHz, mono, 4 bytes/frame | Float32, 48 kHz, LR, 8 bytes/frame |
| Capacidade, período e latência informada | 1.056 frames, 10 ms, 0 reportado | 1.056 frames, 10 ms, 0 reportado |
| Aquisições / liberações | 372 / 372 | 374 / 374 |
| Frames adquiridos / liberados | 178.560 / 178.560 | 180.096 / 180.096 |
| Captura aceita / descarte inicial | 371 pacotes contíguos, 178.080 frames / 1 pacote, 480 frames com DATA_DISCONTINUITY | — |
| FIFO terminal | 178.080 frames escritos, zero drops | 176.160 frames lidos, zero underruns internos |

### Rejeição temporal

O histórico registrou 37 leituras por papel sem sobrescrita, 37 válidas no render e 15 válidas, 21 provisórias e uma rejeitada na captura. Frequências constantes de 192.000 unidades/s na captura e 384.000 no render, com HRESULTs S_OK nas leituras. As provisórias ocorreram nos índices 5–20, 24–25, 29 e 31–32. A idade do QPC lido na captura variou de 0,2423 a 19,9809 ms, mediana 17,2441 ms. No render, de 0,0003 a 0,0859 ms, mediana 0,0006 ms. O maior desvio curto no render foi 1,5936 frame. Na captura, o maior desvio curto foi 480,4896 frames.

A leitura de captura 37 retornou `InvalidPosition` com motivo `WindowRateOutsideTolerance`. A posição bruta foi 714.240 unidades e o QPC 236.026.844.357 × 100 ns. Entre as leituras 36 e 37, o QPC avançou 100,5913 ms e a posição aumentou 19.200 unidades, equivalentes a 4.800 frames. O esperado era 4.828,3824 frames. O erro curto de 28,3824 excedeu o limite nominal de 26,141912, mas ainda cabia na margem de fase do curto prazo. A decisão de parada veio da janela móvel, que usa o limite nominal sem margem de fase.

| Série | Duração da janela | Frames observados | Frames esperados | Resíduo em frames | Limite em frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| Clock de captura, móvel rejeitada | 2,0205647 s | 96.480 | 96.987,1056 | −507,1056 | 486,935528 |
| Pacotes contíguos, móvel | 2,0305572 s | 97.440 | 97.466,7456 | −26,7456 | 489,333728 |
| Clock de captura, acumulada | 3,6205407 s | 173.760 | 173.785,9536 | −25,9536 | 870,929768 |
| Pacotes contíguos, acumulada | 3,7005574 s | 177.600 | 177.626,7552 | −26,7552 | 890,133776 |
| Clock de render, primeira à última leitura | 3,6064182 s | 173.110 | 173.108,0736 | +1,9264 | — |

O limite móvel foi excedido por 20,170072 frames. As duas janelas móveis têm âncoras e durações diferentes. Seus resíduos diferem em 480,36 frames, valor próximo dos 480 frames de um período. Os acumulados da captura e dos pacotes mostram −149,34 e −150,63 ppm em relação à respectiva referência nominal; o acumulado do render mostra +11,13 ppm. São resíduos descritivos em trechos distintos, sem o intervalo de incerteza e a corroboração exigidos pelo estimador. A diferença móvel é compatível com uma variação de fase. O JSON não identifica a causa física, não demonstra perda de um pacote e não resolve as falhas históricas anteriores. Houve 15 janelas de clock e 170 de pacotes confirmadas pela política nominal B1 antes da parada. A corroboração terminal ficou indisponível após a rejeição.

### Cortes, frescor e ocupação

Das 34 consultas, 20 foram `Available` e 14 `OwnerBusy`. Os motivos do diagnóstico foram 12 `Waiting`, oito `PhaseChanged`, quatro `CheckpointUnavailable` e dez `EvidenceExpired`. Nesse modo, `EvidenceExpired` descreve tempo desde o último corte coerente em uma consulta que encontrou owner ocupado; a falha terminal registrada foi a rejeição do clock. Nos 19 cortes com as três pontas disponíveis, as idades no instante do corte foram 14,6791–113,4714 ms para o clock da captura, 7,0142–97,1202 ms para o de render e 23,0661–33,0022 ms para o pacote. O maior intervalo entre cortes foi 1.651,4855 ms. O último corte precedeu a rejeição em 83,163 ms.

A ocupação nos 20 cortes variou de zero, no corte inicial, a 2.400 frames, com mediana 1.920. Os 19 intervalos adjacentes cobrem 3,636462 s. Ao ponderar os limites de cada intervalo pela duração, o limite inferior da média nesse trecho foi 2.051,593 frames e o superior 2.068,252. A largura mediana dos intervalos foi 8,864 frames, com faixa de 7,615 a 87,006. Os cortes reconciliaram escritos menos lidos e, no render, priming do endpoint + pré-ticket + warmup + tentativas de FIFO.

No último corte coerente, havia 174.240 frames escritos e 171.840 lidos, com 2.400 na FIFO. O priming do endpoint foi de 1.056 frames e o preenchimento inicial comprovado da FIFO foi de 2.400. O corte registrou 480 frames liberados antes do ticket e 2.400 durante warmup, fora das tentativas de leitura. No encerramento, a diferença contábil entre produzidos e lidos foi 1.920 frames. O render liberou 180.096 frames no total, dos quais 176.160 foram leituras da FIFO e 1.056 foram priming do endpoint; restam 2.880 fora das tentativas de FIFO. Esses totais são compatíveis com as categorias medidas no último corte, sem interpolar ocupação ou média entre esse corte e o encerramento.

### Incerteza e próximo passo sob revisão

Nenhuma janela ficou elegível para estimativa de drift. Entre pares válidos nos cortes retidos de uma mesma época, o maior intervalo foi 0,2202324 s. A estimativa exige janela de 3 s, sobreposição mínima de 2,5 s e corroboração do clock com pacotes. Assim, as distribuições de centros, limites, sinais e larguras em ppm têm zero amostras. Os resíduos nominais da tabela não os substituem.

Como análise de sensibilidade, para uma janela **hipotética** de 3 s a 48 kHz, com os dois avanços nominais e erro por ponta igual nas duas séries de clock, 480 frames por ponta dariam faixa de aproximadamente −13.245 a +13.423 ppm, largura de 26.668 ppm. Com 5 frames por ponta, a largura ainda seria 277,78 ppm. Com 2 frames, seria 111,11 ppm. Largura de 200 ppm nessa condição pressupõe aproximadamente 3,6 frames de incerteza por ponta, cuja validade física a coleta não demonstrou. O orçamento de um período é uma hipótese de sensibilidade, e não foi reduzido para obter aprovação.

O menor passo a avaliar em revisão é uma observação SILENT nominal que preserve a separação entre tempo de chegada e avanço dos clocks e dos pacotes durante os trechos provisórios. É preciso justificar como obter segmentos coerentes suficientes ou decidir se uma rota nominal limitada atende aos requisitos de segurança sem observações de drift. A rejeição existente continua válida. A tentativa permanece sem aprovação. Correção física, monitoramento audível e F3 seguem sujeitos à revisão.

O JSON numérico original e o log do runner ficam em `artifacts/wasapi-drift-diagnostic`, ignorados pelo Git. SHA-256 do JSON `E236D02F37364D1C729EC3A58CA04693EF16EB3B74E02584306EBB9843801C26`. Sem nomes, IDs de endpoints, voz ou PCM no relatório. Esta etapa preservou a prova 4, o código, os testes manuais e as evidências das regressões anteriores. Não houve nova tentativa.

## F2-I6B2B-DRIFT — controle com evidência intervalar e diagnóstico SILENT

Os dois owners continuam exclusivos para COM e para seus lados da FIFO. O mesmo lote do checkpoint inclui a leitura de clock devida, os metadados do último pacote aceito após ReleaseBuffer e, na captura, os frames de preenchimento e o ppm efetivamente aplicado. Versões e ticket vinculam essas evidências e a ocupação ao mesmo corte QPC. O coordenador lê somente valores publicados. O modo padrão B1 continua com diário e controle desativados.

### Como a estimativa é obtida

- A política B1 continua validando HRESULT, identidade, frequência/unidade, frescor, avanço, flags, continuidade e as janelas independentes da captura. O render usa a política estrita. Aquisições sem liberação integral, timestamps incertos e rejeições existentes conservam o comportamento de parada. Uma leitura provisória de captura muda a época de observação e descarta as âncoras de drift. Um intervalo que cruze essa mudança é recusado mesmo se o coordenador só voltar a observar depois que o clock estiver válido.
- A ponte usa a posição e a frequência de cada `IAudioClock` e a posição em frames do primeiro frame de cada pacote aceito. Cada série tem sua própria origem. A divisão dos deltas de posição pela frequência ou taxa correspondente, e pelo delta do próprio QPC, fornece uma taxa relativa. O alvo usa janelas de 3 s no QPC dos cortes, com sobreposição dos três intervalos de pelo menos 2,5 s. Cada ponta atual precisa ter idade até 500 ms. O estimador estrito antigo e a confirmação nominal de B1 são preservados. [GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition), [GetFrequency](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency), [GetBuffer de captura](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer).
- Se a incerteza de cada ponto é `e` frames, o avanço de cada janela admite erro de `±2e`. O intervalo do pacote deve corroborar o do clock da captura. A interseção serve como verificação de consistência e não reduz artificialmente a incerteza do clock. A razão render/captura usa `render mínimo / captura máxima` e `render máximo / captura mínima`, em ppm relativamente à razão nominal. O ajuste depende de um modelo de erro que cubra os pontos e de taxa suficientemente estável nas janelas. A grade dos fakes usa quantização subframe, jitter determinístico e dois frames de incerteza por observação. O teto para autorizar Tracking nos fakes é largura de 200 ppm, além dos demais critérios.
- O replay da prova 3 conserva a fase de cerca de um período e a classificação provisória. O maior erro curto de captura da prova 4 foi 480,792 frames. Um orçamento analítico ilustrativo de um período de 480 frames por ponta dá uma incerteza de aproximadamente ±4.149 ppm só na captura ao longo dos 4,8199688 s daquela série acumulada. Com o mesmo orçamento nos dois clocks, a largura aproximada da razão em uma janela de 3 s a 48 kHz é 26.667 ppm. Esses valores explicam por que a janela nominal de 0,5% e a corroboração da prova 4 não autorizam correção sub-500 ppm. O período do dispositivo usado no diagnóstico futuro é uma hipótese para análise de sensibilidade. A calibração física continua pendente.

### Preenchimento e comando

| Marco | Contabilidade e ação |
| --- | --- |
| Priming do endpoint | O render preenche seu buffer antes de Start e libera com SILENT. Esses frames não entram na FIFO. |
| Aquecimento nominal | A captura alimenta a FIFO até pelo menos metade da capacidade com o FIR existente. Antes disso, o render libera SILENT sem tentativa de leitura da FIFO; essas liberações têm contador separado, assim como as anteriores ao ticket. |
| FIFO liberada para leitura | A captura registra o preenchimento real. O render pode consumir. Underruns enquanto se aguarda Tracking ficam em contagem de startup. A correção permanece zero até haver intervalo de ocupação e evidência de clock suficientes. |
| Tracking | O coordenador suaviza os limites inferior e superior da ocupação, combina a faixa de erro com o intervalo da razão e escolhe a correção de menor magnitude sustentada pelos limites. Se a faixa incluir zero, a contribuição correspondente pode permanecer neutra. A integral só reage a desvio de ocupação com sinal estabelecido. |
| Falha | Perda de histórico, fronteira da FIFO, drops, underruns após ativação, invalidação ou estimativa perdida fora da espera revogam a entrega. Sem corte renovável, o comando expira em até 500 ms. O aquecimento sem evidência termina em até 8 s de tempo observado. |

O comando imutável inclui geração, marca QPC do corte e ppm. Só a captura o lê e chama `SetTargetPpm` antes de processar o próximo pacote liberado, com ticket e frescor confirmados. O comando respeita ±500 ppm e slew de 25 ppm/s. O FIR usa seu próprio slew por frame, por isso o relatório distingue razão solicitada, aplicação e idade. Comando e processamento preservam a ordem de Stop, invalidação e cleanup. Parada ou erro não reinicia nem reprima automaticamente. Get/Release/Stop seguem as proteções de B1 e o guard continua retido enquanto não houver quiescência e liberação confirmadas.

A publicação dos owners usa valores e anéis pré-alocados. O coordenador guarda até 36 âncoras e 128 amostras numéricas recentes; a consulta tem cadência mínima de 100 ms. As alocações de snapshots/comandos ficam fora da thread de amostras. Toda liberação de render permanece SILENT. Não há encoder de saída, cópia de PCM para WASAPI ou armazenamento de voz.

### Próxima prova física preparada para revisão

O próximo ensaio é uma **única coleta diagnóstica SILENT de 10 s, em razão nominal**, sem habilitar controle no hardware. A implementação conserva a janela de cinco segundos e todas as evidências da prova anterior. O diagnóstico usa o limite de duração já disponível na sessão, com até cerca de 100 consultas no período e históricos limitados. A correção está disponível somente para backend injetado de teste.

O runner dedicado é `WasapiNativeSilentTests.AuthorizedEndpointsCollectSilentDriftEvidenceAtNominalRatio`, marcado `Explicit`. Após revisão e autorização específica de Start, confirmar os sete casos já registrados, app fechado, ausência de chamadas/outros testes e nomes exatos e únicos dos dispositivos ativos nas direções corretas. As variáveis do processo são `WAVESTUDIO_DRIFT_DIAGNOSTIC_START_AUTHORIZED`, `WAVESTUDIO_DRIFT_DIAGNOSTIC_PREREQUISITES_CONFIRMED`, `WAVESTUDIO_DRIFT_DIAGNOSTIC_CAPTURE_NAME` e `WAVESTUDIO_DRIFT_DIAGNOSTIC_HEADSET_NAME`. O teste verifica os dois consentimentos antes da pré-verificação de dispositivos. Selecionar pelo nome exato do teste em Release x64 e conferir o filtro antes de uma execução autorizada.

A aceitação da **coleta** exige duração, Stop/Release confirmados, `TransportObserved` e continuidade confirmada pela captura, pelo menos 60 cortes coerentes e dez janelas estimáveis distribuídas num trecho com pelo menos quatro segundos entre primeira e última. O teste também exige zero comandos, zero atualizações de Tracking e zero ppm aplicado. O JSON de `artifacts/wasapi-drift-diagnostic` contém formatos, tempos, contagens, janelas e âncoras, fase, faixas de ocupação e de ppm, HRESULTs, perdas/underruns, priming e encerramento, sem nomes, IDs ou áudio. Uma janela ampla ou instável serve para identificar limitação de evidência. Para uma prova futura com correção, a análise deve justificar um limite de erro por ponto, demonstrar intervalos repetíveis compatíveis com 200 ppm de largura, distinguir fase de drift e obter nova autorização. A tentativa e seu resultado estão registrados na seção F2-I6B2B-DIAG-PROOF acima.

## F2-I6B2A-CHECKPOINT — corte coerente dos dois owners MTA

`WasapiSilentSession` pode habilitar `WasapiCheckpointLedger` explicitamente com um backend injetado. A prova B1 usa a opção padrão desativada. O diário observa a mesma fila e as mesmas chamadas que já existem na sessão. Ele não copia ou guarda amostras, não altera o FIR, não alimenta estimador ou controlador e não participa de `TransportObserved`. O render continua liberando os buffers com SILENT.

Cada owner escreve em seu próprio diário. A identidade do ledger corresponde a um único ticket, à geração da seleção e ao par de endpoints imutável daquele ticket. Cada lote abrange até 32 pacotes de captura ou um preenchimento de render, incluindo o priming antes de Start. A sequência de eventos avança a cada alteração da contabilidade. Uma passagem sem alteração atualiza somente a última confirmação. O QPC local das fronteiras é medido em unidades de 100 ns. As marcas de `GetBuffer` da captura e de `IAudioClock` continuam nas suas séries originais.

| Ordem do corte | Condição observada |
| --- | --- |
| 1. Owner abre o lote | A versão passa a ímpar antes de consultar padding ou adquirir um buffer. O QPC de abertura precede as mutações da FIFO. |
| 2. Owner fecha o lote | Confirma o ReleaseBuffer, conclui a produção ou o consumo e registra contadores e QPC final. A versão volta a par depois da publicação. Liberação incerta ou falha aborta a confirmação. |
| 3. Coordenador indica o corte | Lê as duas versões pares, toma o QPC do corte e lê os estados e, se solicitado, os eventos do intervalo. |
| 4. Coordenador confirma | Uma barreira de memória precede a releitura das versões. Aceita se ambas continuarem iguais e o ticket ainda estiver válido. |

A abertura e o fechamento usam operações interlocked. Para cada papel, as duas leituras da versão cercam o instante escolhido no passo 3. Uma operação que tenha cruzado esse instante teria mantido a versão ímpar ou alterado a versão entre as leituras. Assim, um corte aceito contém as operações concluídas de ambos os papéis, sem buffer emprestado. Eventos que começam depois de uma confirmação podem pertencer ao próximo corte. O QPC tem resolução finita, então uma marca igual à do corte pode aparecer no início do lote seguinte sem que o lote tenha ocorrido antes do corte lógico. A contagem e as versões resolvem essa inclusão. O coordenador faz até três tentativas, não invoca COM, não espera pelos owners e não impõe um watermark ao WASAPI. Os owners não aguardam o coordenador.

| Contabilidade | Unidade e relação |
| --- | --- |
| Captura nativa | Pacotes e frames da entrada adquiridos e liberados. Na fronteira válida, adquiridos = liberados e liberados = aceitos + descarte inicial, em ambas as unidades. |
| Produção FIFO | Frames no formato de saída efetivamente escritos. A tentativa é a soma de escritos e drops. Um pacote aceito alimenta a conversão e pode produzir uma quantidade diferente de frames de saída. |
| Consumo FIFO | Frames de saída efetivamente lidos. A tentativa é a soma de lidos e underruns da FIFO. |
| Render nativo | Pacotes e frames de saída liberados. Frames liberados = priming + render silencioso que não consultou a FIFO antes do ticket + tentativas de leitura. O priming não popula a FIFO. |
| Ocupação | Frames escritos menos lidos em um corte coerente, entre zero e a capacidade. |

**Intervalo e média temporal.** A consulta pontual entrega apenas ocupação. Para a média, os dois cortes precisam ser do mesmo ledger e o histórico precisa conter todas as sequências entre eles. Cada lote informa um intervalo `[início, conclusão]` que contém as mutações reais da FIFO. A área começa com `ocupação inicial × duração`. Uma escrita adiciona `frames × (fim − instante da mutação)` e uma leitura subtrai a mesma expressão. Para obter o limite inferior, aplicam-se as escritas no fim dos respectivos lotes e as leituras no início. O limite superior faz o inverso. Isso contém a área de qualquer interleaving, inclusive lotes simultâneos, e o intervalo é limitado por `[0, capacidade × duração]`. O resultado expõe os dois limites na unidade frame × 100 ns e o denominador na unidade 100 ns. Limites iguais determinam a média na escala discreta do QPC. Limites distintos preservam a incerteza sobre a ordem e o instante das mutações.

**Ausência de evidência e falhas.** Owner em operação ou sem primeira confirmação deixa o corte indisponível. Uma confirmação com mais de 500 ms de idade, incluindo confirmação ausente após esse prazo, é identificada na leitura estável e encerra a sessão opt-in. Esse prazo é somente diagnóstico. Sequência inválida, tempo regressivo ou futuro, contabilidade incompatível, overflow ou falha de operação invalidam a observação. Uma falha de integridade observada invalida o ticket e usa a parada existente. Get/Release falho mantém a causa original da operação. O guard continua retido até os dois owners encerrarem com liberação confirmada. Release incerto preserva recursos e lease e não dispara uma nova tentativa de Release. Stop, cancelamento e invalidação da seleção encerram imediatamente a possibilidade de publicar novos cortes. Um intervalo com histórico sobrescrito, baseline incompatível ou geração errada é recusado sem retornar dados parciais. Uma nova consulta pontual pode usar os acumulados se a sessão continuar válida.

**Orçamento.** Cada anel tem 256 entradas numéricas de 32 bytes, totalizando 16 KiB para os dois históricos. Sem alocação recorrente, I/O ou lock bloqueante na publicação dos owners. Uma consulta bem-sucedida cria um snapshot imutável no coordenador. A leitura do intervalo percorre no máximo 256 entradas por papel e por tentativa. A capacidade, os períodos e os limites dos buffers da prova B1 seguem os mesmos. A validação desta etapa usa relógio determinístico e owners MTA com fakes. Os números da prova 4 continuam cumulativos e não permitem reconstruir retrospectivamente uma média de ocupação, latência fim a fim ou drift físico.

## F2-I6B1-PROOF-4 — resultado físico, 18/11/2026

Única tentativa autorizada, Release x64 de `f72304e`, preflight em `d41a6f7`, SDK 10.0.404. O proprietário confirmou headset conectado, fechamento normal e ausência de chamadas e testes. Os endpoints foram verificados ativos e únicos por direção. O apphost executou somente `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport`, com `--explicit only --filter-method` e opt-in restrito ao processo. A política Restricted foi preservada. Runner código 0, um teste aprovado, 5,595 s totais. `DurationReached=true` indica conclusão do temporizador configurado para 5 s. O QPC mediu 49.997.441 × 100 ns (4,9997441 s), 0,2559 ms abaixo de 5 s exatos.

| Evidência | Captura | Render SILENT |
| --- | --- | --- |
| Mix exato | Float32 / 48 kHz / mono, máscara 4, 4 bytes/frame | Float32 / 48 kHz / LR, máscara 3, 8 bytes/frame |
| Capacidade / padding final | 1.056 / 0 frames | 1.056 / 576 frames |
| Período padrão / latência reportada | 100.000 / 0 × 100 ns | 100.000 / 0 × 100 ns |
| Initialize / Start / último GetBuffer / último ReleaseBuffer / Stop | Confirmado / S_OK / S_OK / S_OK / S_OK | Confirmado / S_OK / S_OK / S_OK / S_OK |
| Pacotes adquiridos / liberados | 500 / 500 | 502 / 502 |
| Frames adquiridos / liberados | 240.000 / 240.000 | 241.536 / 241.536 |
| Aceitos / descarte inicial | 499 pacotes, 239.520 frames / 1 pacote, 480 frames | Não se aplica |
| Flags agregadas / descarte inicial | 0x1 / 0x1 DATA_DISCONTINUITY | Sempre SILENT |
| Posição e QPC do descarte | 294.428.640 frames / 161.826.458.080 × 100 ns | Não se aplica |
| Último pacote | 294.668.160 frames / 161.876.351.729 × 100 ns | Não se aplica |
| Priming | 0 | 1.056 frames |
| Histórico total / provisórias / sobrescritas | 49 / 6 / 0 | 49 / 0 / 0 |
| Avanços válidos contabilizados | 42 | 48 |
| Última posição / frequência de IAudioClock | 944.640 unidades / 192.000 unidades/s | 1.882.008 unidades / 384.000 unidades/s |
| Último QPC de IAudioClock | 161.875.741.839 × 100 ns | 161.875.691.593 × 100 ns |
| Liberação / buffer em voo / erro de limpeza | Confirmada / não / nenhum | Confirmada / não / nenhum |

Os dois históricos têm sequência de 1 a 49, com HRESULTs S_OK e `Rejection=null`. Os deltas, restos, idades, erros e limites foram recalculados offline para ambos os clocks. A captura ficou provisória nos índices 14–19. O maior erro curto foi 480,792 frames na leitura 14, com limite nominal de 25,99604 e margem de 480 frames (período de 10 ms). Na leitura 19 o resíduo acumulado já era +0,7824 frame. O maior erro curto em leitura estrita da captura foi 0,7104 frame (limite 26,003552). No render, foi 1,7376 frame (limite 26,021312). A captura confirmou 29 janelas de clock e 298 de pacotes. O maior erro absoluto entre as janelas de clock foi 479,3088 frames, dentro do limite de 486,796544 nessa janela. O render manteve `CaptureTiming=null`.

| Série | Âncora QPC (× 100 ns) | QPC final (× 100 ns) | Duração | Avanço em frames | Resíduo em frames | Limite nominal |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Clock, móvel | 161.854.741.894 | 161.875.741.839 | 2,0999945 s | 100.800 | +0,264 | 505,99868 |
| Pacotes, móvel | 161.856.251.927 | 161.876.351.729 | 2,0099802 s | 96.480 | +0,9504 | 484,395248 |
| Clock, acumulado | 161.827.542.151 | 161.875.741.839 | 4,8199688 s | 231.360 | +1,4976 | 1.158,792512 |
| Pacotes, acumulado | 161.826.552.102 | 161.876.351.729 | 4,9799627 s | 239.040 | +1,7904 | 1.197,191048 |

Os frames na âncora/fim da janela móvel foram 130.560/231.360 no clock e 294.571.680/294.668.160 nos pacotes. Nos acumulados foram 0/231.360 no clock e 294.429.120/294.668.160 nos pacotes. A sobreposição de 4,8199688 s deu `|231360 − 239040 × (48199688 / 49799627)| = 0,2352788707` frame, dentro de 1.158,792512. `Confirmed=true`. O relatório confirma 499 pacotes contíguos, igual aos 499 aceitos, com próxima posição esperada 294.668.640. O avanço entre os inícios dos pacotes, mais o último pacote de 480 frames, fecha em 239.520 frames. Somado o descarte de 480 frames, fecha com os 240.000 liberados.

FIFO nominal de 4.096 frames, 239.520 produzidos, 239.040 lidos, zero drops e 960 frames de underrun interno. Após 1.056 frames de priming, o render liberou mais 240.480 frames SILENT. Esses contadores e a latência reportada pelo WASAPI não são medições de latência fim a fim. O resultado registrou `Completed`, `MovementProven` e `TransportObserved` verdadeiros e `ResourcesRetained=false`. Os dois streams confirmaram Stop e liberação.

O JSON original fica local em `artifacts/wasapi-silent`, ignorado pelo Git, sem nomes, IDs ou PCM. Nenhuma voz foi armazenada. Código, parâmetros e proteções preservados, sem repetição da prova ou regressão completa. A tentativa verifica a política temporal no cenário testado e não estabelece causa para as rejeições anteriores, latência acústica ou controle de drift. F2-I6B2 e F3 aguardam revisão.

## F2-I6B1-CLOCK-WINDOW — validação temporal da captura silenciosa, 18/11/2026

O replay offline usa as 11 leituras reais de cada papel. A fixture subtrai a mesma origem das marcas QPC e locais, retém posições, frequência e HRESULTs e não contém PCM ou IDs. O modo estrito reproduz a rejeição: 21.120 unidades a 192.000 unidades/s equivalem a 5.280 frames. Os 120,013 ms do QPC implicam 5.760,624 frames a 48 kHz. O resíduo assinado é −480,624 frames, com limite de 30,80312. Na captura, os nove intervalos anteriores avançaram exatamente 4.800 frames cada, com erro absoluto máximo de 0,5472 frame; o resíduo acumulado desde a primeira leitura foi +0,9648 antes da rejeição e −479,6592 na rejeição. No render, o resíduo terminal acumulado foi −0,5952 frame e o erro máximo por leitura foi 1,368. A idade QPC da captura caiu de 20,0004 a 0,3637 ms, com 100,3763 ms entre as leituras no QPC local e 110 ms de posição convertida. A variação é compatível com uma mudança de fase de aproximadamente um período de 10 ms, sem identificar sua origem ou provar drift sustentado. O histórico físico termina em ~1,11 s e não contém a série de pacotes para confirmar continuidade. Não é possível determinar o ramo da prova 2 com esses dados.

`GetDevicePeriod` fornece o período de agendamento padrão do modo compartilhado. Para esta prova, a margem em frames é `min(capacidadeFrames, teto(período100ns × taxa / 10.000.000))`. O período de 10 ms dá 480 frames a 48 kHz. A margem é um limite conservador da prova fundamentado no quantum consultado, sem garantia de fase para `GetPosition`. A posição de pacote e seu timestamp correspondem ao primeiro frame de `GetBuffer`. Eles pertencem a uma série distinta da posição em DeviceUnits de `GetPosition`; nenhum timestamp ou posição é reconstruído a partir de contadores locais. [GetDevicePeriod](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getdeviceperiod), [GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition), [GetFrequency](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency), [GetBuffer](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer).

A política é exclusiva da captura nativa SILENT. O modo estrito continua padrão no adaptador, no render e nas simulações existentes. Identidade/geração, HRESULTs/qualidade, flags, unidade/frequência, frescor, QPC crescente, regressão, overflow, limite de intervalo de 2 s por leitura e prazo de progresso de 500 ms mantêm rejeição e retenção anteriores. Em captura SILENT, o erro por leitura pode ficar até uma margem de período acima de `0,005 × framesEsperados + 2`. O resíduo assinado desde uma primeira âncora fixa também precisa permanecer dentro desse envelope. Exceder o limite nominal por leitura ou no acumulado marca a observação como provisória. Não há reajuste de âncora, posição, tolerância global ou alimentação do estimador. Com essa política, a leitura 11 da prova 3 fica provisória e a prova histórica continua sem confirmação.

Cada série guarda âncoras numéricas pré-alocadas em um anel de 32 posições, com espaçamento mínimo de 100 ms em QPC. A partir de 2 s, cada nova leitura/pacote é comparado à âncora armazenada mais recente com idade de pelo menos 2 s. O intervalo permitido vai de 2 a 2,5 s, com margem de fase **zero** na verificação de taxa. O limite permanece `0,005 × framesEsperados + 2`. Se a primeira confirmação não chegar em 2,5 s desde a âncora inicial, a operação encerra. Permanecem os prazos de progresso/frescor. A âncora fixa e as janelas móveis rejeitam, respectivamente, erros acumulados que esgotam o envelope de fase e taxas sustentadas fora do limite mesmo depois de uma janela válida.

A validação de pacotes ocorre no owner. Salto, duplicação, sobreposição, overflow de posição ou QPC não crescente fazem o owner rejeitar o pacote antes da leitura e liberar zero frames conforme o contrato. O pacote descartado na inicialização fica fora da evidência. O primeiro pacote normal com ReleaseBuffer integral ancora a série. Cada pacote seguinte deve iniciar em `posição anterior + frames anteriores`. A série usa seu próprio QPC, resíduo e janela móvel. Um pacote que falha a validação de taxa após a liberação integral não é entregue à FIFO. A contabilidade de pacotes contíguos deve coincidir com a dos aceitos.

Ao encerrar, as duas séries precisam de evidência recente, janelas móveis válidas terminando nas últimas observações e taxa nominal no acumulado, sem margem de fase. Os acumulados precisam se sobrepor por pelo menos 2 s no QPC. Cada avanço é normalizado pela própria duração para comparar os dois avanços na duração partilhada, com o limite nominal aplicado a essa duração. Isso corrobora as taxas por deltas sem igualar origens de posição ou extrapolar um pacote descartado. Pode rejeitar conservadoramente oscilações que ainda respeitam cada janela isolada. A aprovação também depende dos critérios existentes de duração, movimentos válidos em ambos papéis, frames aceitos/produzidos/lidos, contabilidade, Stop e liberação. O diagnóstico registra envelopes, resíduos, âncoras, contadores e motivos sem I/O ou alocação recorrente nos owners. O snapshot é publicado após join.

A sequência sintética de cinco segundos com recuperação valida o teste, sem evidência de que o comportamento observado na prova 3 continue ou se recupere dessa forma no dispositivo. Esta política não mede latência fim a fim nem habilita controle de drift ou reprodução audível. Prova 4 somente após revisão e autorização específica. F2-I6B2/F3 aguardam evidência física.

## F2-I6B1-PROOF-3 — resultado físico, 18/11/2026

Tentativa física única autorizada, Release x64 de `97b2c99`, preflight em `38d49d2`, .NET SDK 10.0.404. O proprietário confirmou as condições imediatas. A seleção foi verificada ativa e única por direção, com app e outros runners ausentes. O apphost executou apenas `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport` com `--explicit only --filter-method`, usando as variáveis opt-in somente no processo. A política Restricted foi preservada. O runner reportou um teste, uma falha, código 2 e duração total de 1.721 ms. A observação do transporte foi de 11.103.045 × 100 ns (1.110,3045 ms).

| Evidência | Captura | Render SILENT |
| --- | --- | --- |
| Mix exato | Float32 / 48 kHz / mono, máscara 4, 4 bytes/frame | Float32 / 48 kHz / LR, máscara 3, 8 bytes/frame |
| Capacidade / padding final | 1.056 / 480 frames | 1.056 / 576 frames |
| Período padrão / latência reportada | 100.000 / 0 × 100 ns | 100.000 / 0 × 100 ns |
| Initialize / Start / último GetBuffer / último ReleaseBuffer / Stop | Confirmado / S_OK / S_OK / S_OK / S_OK | Confirmado / S_OK / S_OK / S_OK / S_OK |
| Pacotes adquiridos / liberados | 112 / 112 | 112 / 112 |
| Frames adquiridos / liberados | 53.760 / 53.760 | 54.336 / 54.336 |
| Aceitos / descarte inicial | 111 pacotes, 53.280 frames / 1 pacote, 480 frames | Não se aplica |
| Flags agregadas / descarte inicial | 0x1 / 0x1 DATA_DISCONTINUITY | Sempre SILENT |
| Posição e QPC do descarte inicial | 189.703.200 frames, 140.008.828.289 × 100 ns | Não se aplica |
| Último pacote de captura | 189.756.480 frames, 140.019.928.187 × 100 ns | Não se aplica |
| Priming | 0 | 1.056 frames |
| Histórico total / sobrescritas | 11 / 0 | 11 / 0 |
| Leituras válidas / avanços válidos | 10 / 9 | 11 / 10 |
| Última posição / frequência de IAudioClock | 215.040 unidades / 192.000 unidades/s (rejeitada) | 418.240 unidades / 384.000 unidades/s (aceita) |
| Último QPC de IAudioClock | 140.020.118.187 × 100 ns | 140.020.048.887 × 100 ns |
| Últimos GetFrequency / GetPosition | S_OK / S_OK | S_OK / S_OK |
| Liberação / buffer em voo / erro de limpeza | Confirmada / não / nenhum | Confirmada / não / nenhum |

O JSON tem índices consecutivos de 1 a 11 nos dois papéis. Todos os HRESULTs de relógio foram S_OK, com qualidade Accurate e flags de clock zero. A reconstituição offline confirmou os vínculos de âncora, deltas, restos e decisões do histórico. Entre as leituras aceitas, o maior erro na captura foi 0,5472 frame com limite de 25,997264 (índice 6). No render, foi 1,368 frame com limite de 26,02184 (índice 11). O render reportou `Rejection=null`. O maior erro absoluto da captura incluindo a rejeição foi 480,624 frames.

### Leitura rejeitada e sua âncora

| Campo | Captura 10, última aceita | Captura 11, rejeitada |
| --- | --- | --- |
| Desde Start (× 100 ns) | 10.090.083 | 11.093.846 |
| Posição / frequência | 193.920 / 192.000, DeviceUnits | 215.040 / 192.000, DeviceUnits |
| QPC da leitura (× 100 ns) | 140.018.918.057 | 140.020.118.187 |
| QPC local / idade (× 100 ns) | 140.019.118.061 / 200.004 | 140.020.121.824 / 3.637 |
| GetFrequency / GetPosition | S_OK / S_OK | S_OK / S_OK |
| Unidade / qualidade / flags de clock | DeviceUnits / Accurate / 0 | DeviceUnits / Accurate / 0 |
| Posição e QPC da âncora | 174.720 / 140.017.918.128 | 193.920 / 140.018.918.057 |
| Último QPC de avanço na âncora (× 100 ns) | 140.017.918.128 | 140.018.918.057 |
| Frames acumulados / resto na âncora | 38.400 / 0 | 43.200 / 0 |
| Delta posição / delta QPC (× 100 ns) | 19.200 / 999.929 | 21.120 / 1.200.130 |
| Avanço / esperado / erro absoluto (frames) | 4.800 / 4.799,6592 / 0,3408 | 5.280 / 5.760,624 / 480,624 |
| Limite / resto após aceitar | 25,998296 / 0 | 30,80312 / não aplicado |
| Resultado | Valid | InvalidPosition / AdvanceOutsideTolerance |

Reprodução offline da rejeição com os valores do adaptador:

- Posição avançada: `215040 - 193920 = 21120` unidades. Conversão inteira: `(21120 × 48000 + 0) / 192000 = 5280` frames.
- QPC avançado: `140020118187 - 140018918057 = 1200130` ticks de 100 ns. Intervalo: `0,120013 s`. Avanço esperado: `48000 × 0,120013 = 5760,624` frames.
- Erro: `|5280 - 5760,624| = 480,624` frames. Limite aplicado: `0,005 × 5760,624 + 2 = 30,80312`. A comparação reproduz `AdvanceOutsideTolerance`.

Entre as duas leituras, o QPC local avançou 100,3763 ms, o QPC reportado avançou 120,013 ms e o incremento da posição equivaleu a 110 ms pela frequência reportada. A idade caiu de 20,0004 ms para 0,3637 ms. O teste confirma a divergência naquele intervalo, sem identificar sua origem, nem o motivo exato da prova 2. A posição de pacote de `GetBuffer` é em frames e permanece separada da posição em DeviceUnits de `IAudioClock`.

O estágio `Capture` registrou `InvalidOperationException` sem HRESULT de falha. A captura fecha em 111 + 1 = 112 pacotes e 53.280 + 480 = 53.760 frames. A FIFO nominal de 4.096 frames registrou 53.280 produzidos, 52.320 lidos, 0 drops e 960 de underrun, sem medida de latência fim a fim ou controle de drift. `Completed=true`, `ResourcesRetained=false`, `DurationReached=false`, `MovementProven=false` e `TransportObserved=false`. A prova de cinco segundos permanece sem aprovação.

O JSON original permanece local em `artifacts/wasapi-silent`, ignorado pelo Git, sem nomes, IDs ou PCM. A análise offline usou os dados da única tentativa. Código, parâmetros e proteções foram preservados, sem nova tentativa ou regressão completa. A análise da diferença de clock precede qualquer mudança de política ou avanço para F2-I6B2.

## F2-I6B1-CLOCK-DIAG — motivos e histórico por stream, 18/11/2026

Na prova 2, a captura apresentou dois avanços válidos antes de `InvalidPosition`. O último `GetPosition` retornou S_OK, posição 80.640, frequência 192.000 e QPC 122.054.762.600 × 100 ns. A hipótese de espera de startup sem âncora fica excluída pelos avanços já aceitos. Faltam a leitura anterior, o resto e o delta usados para distinguir mudança de unidade/frequência, regressão, intervalo acima de 2 s e avanço fora da tolerância. A posição do último pacote de captura, 103.522.080 frames, pertence ao `GetBuffer` e não substitui essa leitura anterior.

A ponte obtém `GetFrequency` e `GetPosition` da própria sessão, retém HRESULTs/UINT64 em `WasapiClockReading` e converte para `DeviceUnits` com `ToSample`. `WasapiRunningClock` aplica a regra de espera inicial e encaminha a leitura ao adaptador. O adaptador valida relógio e timestamp por leitura e o owner encerra se a leitura for rejeitada. O estimador de razão trabalha também com uma janela acumulada quando usado na simulação. Ele não participa da prova silenciosa. S_OK indica que a API obteve a leitura. Posições podem se repetir e a Microsoft descreve efeitos de temporização, mas os contratos não estabelecem o limite de avanço por leitura que usamos. Granularidade e jitter permanecem hipóteses. [GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition), [GetFrequency](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency), [GetBuffer](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer).

A conversão registra o motivo no ramo que toma a decisão, mantendo `NativeClockResult`, a precedência das verificações e a exigência de reset. Distingue unidade, frequência, regressão, intervalo e tolerância, além de startup, identidade, flags, qualidade, QPC inválido, estagnação e overflow. A evidência contém índice, tempo desde Start, HRESULTs, posição, frequência, idade/QPC, unidade, flags, qualidade, âncora anterior, deltas, resto racional e os valores efetivamente calculados para a decisão. Campos de etapas não atingidas ficam nulos. O limite continua `0,005 × frames esperados + 2`, aplicado por leitura, junto do teto de 2 s e do frescor de 500 ms. O código preserva a política de S_FALSE.

Cada owner mantém um buffer circular pré-alocado de 64 leituras, suficiente para a cadência nominal de cerca de 50 leituras na prova de 5 s. A última aceita e a primeira rejeitada ficam separadas dos registros sobrescrevíveis. A rejeitada inclui a âncora anterior. O coordenador publica cópias somente leitura em ordem cronológica, com contador de sobrescritas, após o join dos dois owners. Snapshots sem join omitem o histórico. A leitura do clock não faz I/O nem aloca objetos recorrentes para o diagnóstico. O JSON do teste é serializado fora dos owners, sem nomes, IDs ou PCM.

## F2-I6B1-PROOF-2 — resultado físico, 18/11/2026

Única tentativa física autorizada com Release x64 da correção `c7ff2c9`, SDK 10.0.404 e preflight em `212a60c`. A seleção exata foi confirmada ativa e única por direção. O proprietário confirmou headset conectado, Wave Studio fechado normalmente e ausência de chamadas e testes. A descoberta encontrou apenas `WaveStudio.Tests.WasapiNativeSilentTests.AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport`. Execução com `--explicit only --filter-method` e esse nome completo, variáveis opt-in somente no processo de teste. Runner retornou código 2, uma falha em 941 ms totais.

| Evidência | Captura | Render SILENT |
| --- | --- | --- |
| Mix exato | Float32 / 48 kHz / mono, máscara 4, 4 bytes/frame | Float32 / 48 kHz / LR, máscara 3, 8 bytes/frame |
| Capacidade / padding no snapshot | 1.056 / 480 frames | 1.056 / 576 frames |
| Período padrão / latência reportada | 100.000 / 0 × 100 ns | 100.000 / 0 × 100 ns |
| Initialize / Start | Confirmado / S_OK | Confirmado / S_OK |
| Pacotes adquiridos / liberados | 42 / 42 | 43 / 43 |
| Frames adquiridos / liberados | 20.160 / 20.160 | 21.216 / 21.216 |
| Captura aceita / descarte inicial | 41 pacotes, 19.680 frames / 1 pacote, 480 frames | Não se aplica |
| Flags agregadas / descarte inicial | 0x1 / 0x1 DATA_DISCONTINUITY, posição 103.502.400, QPC 122.050.472.784 × 100 ns | Saída sempre SILENT |
| Último pacote de captura | Posição 103.522.080, QPC 122.054.572.600 × 100 ns | Não se aplica |
| Priming | 0 | 1.056 frames antes do Start |
| Último GetBuffer / ReleaseBuffer / Stop | S_OK / S_OK / S_OK | S_OK / S_OK / S_OK |
| Último GetFrequency | S_OK, 192.000 unidades/s | S_OK, 384.000 unidades/s |
| Último GetPosition | S_OK, 80.640 unidades, QPC 122.054.762.600 × 100 ns | S_OK, 151.240 unidades, QPC 122.054.640.215 × 100 ns |
| Avanços válidos antes da parada / estado final do clock | 2 / InvalidPosition (6) | 3 / Valid (0) |
| Liberação final / buffer em voo / erro de limpeza | Confirmada / não / nenhum | Confirmada / não / nenhum |

O descarte inicial foi contabilizado integralmente. A soma de 19.680 frames aceitos e 480 descartados corresponde aos 20.160 frames liberados no endpoint, em 42 pacotes. A FIFO nominal de 4.096 frames registrou 19.680 produzidos, 18.720 lidos, 0 drops e 960 frames de underrun. A diferença de 960 frames entre produção e leitura é uma contagem distinta do underrun. Esses números e a latência reportada pelo WASAPI não medem latência fim a fim.

O estágio `Capture` registrou `InvalidOperationException` sem HRESULT de falha. O HRESULT S_OK indica sucesso da chamada nativa, enquanto `InvalidPosition` registra a rejeição pelo validador. O relatório conserva a última leitura, sem os deltas anteriores necessários para identificar qual regra causou a rejeição. A observação terminou em 4.125.004 × 100 ns (412,5004 ms), com `DurationReached=false`, `MovementProven=false` e `TransportObserved=false`. A prova de cinco segundos segue sem aprovação. O snapshot registra `Completed=true` e `ResourcesRetained=false`.

O JSON numérico permanece em `artifacts/wasapi-silent`, ignorado pelo Git, sem nomes/IDs de endpoints ou PCM. A tentativa não foi repetida e os critérios de clock e proteção permanecem os mesmos. A evidência sustenta o descarte inicial nessa execução, sem demonstrar drift controlado ou habilitar F2-I6B2.

## F2-I6B1-FIX — descarte na inicialização, 18/11/2026

O WASAPI pode sinalizar DATA_DISCONTINUITY em uma transição de estado ou em uma falha de temporização. A origem da flag registrada na primeira prova permanece indeterminada. O backend pode consumir e descartar somente o primeiro pacote não vazio da sessão se as flags forem DATA_DISCONTINUITY ou DATA_DISCONTINUITY | SILENT. O limite de 500 ms começa imediatamente antes da chamada de Start da captura, inclui o tempo da chamada nativa e usa o mesmo teto da validação de frescor. É uma política desta prova. BUFFER_EMPTY não ocupa essa oportunidade, e um primeiro pacote limpo segue o fluxo normal. [Flags do WASAPI](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/ne-audioclient-_audclnt_bufferflags).

O owner verifica o tamanho anunciado para o próximo pacote e a capacidade do endpoint, além das flags, do QPC fresco e do ponteiro obrigatório se faltar SILENT. Para um pacote descontínuo com dados, lê o conteúdo inteiro no scratch existente e limpa o scratch antes de liberar o pacote. Com SILENT, avança o pacote sem dereferenciar seu ponteiro. A liberação bem-sucedida com o tamanho integral permite avançar. Uma liberação de zero reapresentaria o mesmo pacote. Nenhuma amostra descartada entra no FIR ou FIFO, e a posição e o QPC desse pacote servem apenas para diagnóstico. Descontinuidade subsequente ou fora da janela, timestamp error, bits desconhecidos e metadados inválidos encerram a operação sob o contrato anterior. A saída usa SILENT durante toda a sessão. [GetBuffer](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), [ReleaseBuffer](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-releasebuffer).

O relatório separa os pacotes e frames descartados no startup dos aceitos para processamento, e preserva flags agregadas e flags/posição/QPC do descarte inicial. A aceitação exige a janela completa, ambos Start/Stop e recursos liberados, pelo menos dois avanços válidos de IAudioClock por papel, um pacote aceito e amostras efetivamente produzidas e lidas na FIFO. As contagens aceitos + descartados devem fechar com o endpoint após join. S_FALSE, limites de clock e retenção de recursos permanecem sob os mesmos critérios. Os cinco segundos, o modo SILENT e a janela de autorização física foram preservados.

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
