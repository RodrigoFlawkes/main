# Fundação do motor — F2-I1

Decisão de 13/11/2026, base 189f2e1. Implementação isolada em `WaveStudio.Audio/Engine`, executada por testes com dados sintéticos. Não existe vínculo com Desktop, backend contínuo, abertura de dispositivo ou reprodução física. A Alpha e sua identidade permanecem preservadas; A01–A12 e Windows 11 continuam conforme a aceitação da Fase 1. Produto funcional antes do redesign profissional.

## Pipeline executável e limites

`PCM16 LE / float32 LE → conversão → float32 intercalado → fila SPSC → processamento neutro/proteção → destino em memória`.

| Componente | Contrato implementado |
| --- | --- |
| PcmFloatFormat | Formato imutável, float32, 8–192 kHz, mono ou estéreo lógico frontal L/R. Frame contém todos os canais, na ordem L,R para estéreo |
| PcmFloatConverter | PCM16 little-endian dividido por 32768, ou float32 little-endian; destino de tamanho exato, frames inteiros. Não remapeia canais nem muda taxa |
| SpscFrameBuffer | Array pré-alocado; capacidade potência de dois, 1–65.536 frames. Exatamente um produtor e um consumidor; nenhuma espera/repetição interna para obter espaço/dados |
| NeutralProcessor | Ganho unitário, sem efeitos. Amostras finitas em [-1,1] preservadas, incluindo zero com sinal; NaN/Infinity tornam-se zero, excesso finito é limitado a [-1,1], com contadores |
| SyntheticAudioPipeline | Conecta conversão, fila e proteção; bloco até 4.096 frames, geração/cancelamento e estados. Não cria threads, COM, I/O, gravação, rede ou controles |

Configuração do ensaio: 48 kHz, dois canais, bloco de 256 frames, fila de 1.024 frames. Buffers: 10.240 bytes. Maior configuração permitida: 557.056 bytes de payload (fila + scratch), além dos pequenos objetos fixos do runtime. Alocação ocorre na construção; Start/Reset reutilizam a memória. Zero cópia não é prometido: há cópias delimitadas entre spans e fila.

Um frame parcial, encoding desconhecido, bloco acima do limite ou formato fora do subconjunto gera erro de contrato. Essas chamadas inválidas podem construir exceções; não pertencem ao regime estável medido. Reentrada/múltiplos participantes no mesmo papel são recusados por CAS, sem espera. Um backend futuro deverá validar capacidades antes de iniciar callbacks e respeitar ownership dos spans.

## Overflow, underrun e proteção

- Overflow: aceita somente o prefixo de frames que cabe; descarta o sufixo mais novo e contabiliza OverrunFrames. Nunca sobrescreve dados ainda não consumidos e nunca avança o cursor do consumidor. Um backend não deve esperar ou repetir o envio no callback.
- Underrun: entrega frames disponíveis e completa o restante do destino com zeros; contabiliza UnderrunFrames. O cursor avança apenas pelos frames realmente consumidos, preservando a ordem futura. Zero é dado válido, não um código de erro.
- Parado, cancelado, descartado ou geração obsoleta: entrada recusada e destino de saída zerado. Nenhuma tentativa de dispositivo alternativo, pois este pipeline sequer conhece endpoints.
- Proteção é aplicada após a leitura da fila, inclusive ao preenchimento silencioso. Não equivale a um efeito, ganho de hardware, AGC ou limiter com lookahead; hard clipping pode distorcer. Um futuro bypass de efeitos deverá conservar a proteção. Nesta etapa não há efeitos nem botão de bypass fictício.

## Protocolo de concorrência da fila

Apenas o produtor escreve writeSequence; apenas o consumidor escreve readSequence. Ambos são uint e usam aritmética unchecked módulo 2^32. A distância válida fica entre zero e CapacityFrames, muito abaixo de 2^31. O índice físico é sequência & (capacidade - 1), por isso a capacidade exige potência de dois. Wrap do array e rollover do contador são tratados separadamente e testados próximos de uint.MaxValue.

1. O produtor adquire readSequence, calcula espaço livre, copia o payload e publica writeSequence com Volatile.Write.
2. O consumidor adquire writeSequence, copia somente o payload publicado e publica readSequence com Volatile.Write depois de terminar a cópia.
3. A publicação do produtor impede expor frames antes da cópia; a publicação do consumidor impede reutilizar slots antes de terminar a leitura. Não há escrita concorrente no mesmo slot enquanto o protocolo é respeitado. Uma observação antiga de cursor reduz a quantidade aceita/lida, sem permitir exceder o espaço publicado.

Essa ordenação usa as garantias de leitura/acquire e escrita/release documentadas pela Microsoft em [Volatile](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0). Não depende apenas de acessos ordinários ao array ou da atomicidade de um inteiro. A implementação destina-se ao alvo x64 atual; não declara validação ARM64.

Snapshots tentam amostrar cursores até três vezes: se o cursor do consumidor mudar durante a observação ou a distância for inválida, tentam novamente; após o limite retornam indisponibilidade. Não há loop indefinido nem número fabricado. Totais long são observações atômicas individuais, não uma transação simultânea entre threads. Conservação completa (escritos = lidos + fila) é verificável em quiescência. Snapshot/UI não devem inferir perda por diferenças entre totais colhidos em instantes diferentes.

## Ciclo de vida e geração

Construção inicia Stopped. Start exige parada/quiescência, limpa a memória/contadores, publica token e geração nova antes de Running, e retorna essa geração. Token já cancelado é recusado. A geração acompanha cada Produce/ProducePcm/Render; não é inferida do estado atual do aplicativo.

Produção e consumo possuem gates CAS independentes, entrados antes de acessar buffers e liberados no finally. Stop publica Stopped por Interlocked.Exchange, sem esperar callbacks. TryReset só limpa a memória após parar e observar ambos os gates livres; devolve false quando há trabalho em andamento. Exchange no controle e CAS nos participantes fornecem barreiras completas para o handshake: um participante vê Stopped ou o controle detecta o gate ocupado antes de limpar. Nenhuma thread de callback adquire o lock do plano de controle. Start, Stop, TryReset, Snapshot e Dispose são operações de controle, fora do caminho de áudio.

Cancelamento é consultado antes/depois da conversão/produção ou processamento; a saída é novamente zerada quando a invalidação é observada ao final. Frames já consumidos/contabilizados numa operação posteriormente cancelada continuam nos totais de trabalho executado. Cancelamento não é uma transação atômica com um envio físico: um backend futuro deve revalidar geração/alvo antes de submeter ao dispositivo.

Dispose é idempotente, para e limpa somente em quiescência; se ainda houver participante, retorna erro para o controle aguardar o término e tentar novamente. O backend futuro deverá desregistrar/parar callbacks e aguardar seus workers antes de reutilizar papéis/descartar. Não há descarte/Join no callback. Uma nova geração não autoriza dois produtores ou consumidores simultâneos na mesma instância. Reset não é um comando de controle do Windows.

## Relógios, adaptação e resampling: contrato para os próximos incrementos

Nesta etapa todos os frames pertencem ao relógio sintético do teste. Os formatos de entrada e saída devem ter taxa e layout iguais; demais combinações são rejeitadas. Taxas nominais iguais em dispositivos físicos distintos NÃO garantem relógios iguais. RequireCompatible valida somente o formato, não a identidade dos osciladores. Não conectar diretamente duas placas apenas porque ambas dizem 48 kHz.

O backend posterior deverá oferecer descritor imutável com ID/direção, formato negociado, máscara/ordem dos canais, período em frames, identidade do relógio, posição de frames e timestamp monotônico/QPC, discriminando latência estimada/consultada/medida. Spans de captura são emprestados apenas durante o callback. IDs e geração serão capturados na abertura, sem retarget silencioso. Uma fonte de pico não é uma fonte PCM.

Contrato planejado do adaptador de canais: plano explícito para layouts reconhecidos; mesmas posições preservadas, mono→L/R ou downmix somente por política autorizada e com coeficientes/limites testados. Máscara desconhecida/multicanal sem plano deve ser recusada. Não fabricar stereo físico nem chamar pan de balanço de um microfone mono.

Contrato planejado do resampler: taxa de entrada/saída explícita, número de frames consumidos/produzidos, estado de fase entre blocos, histórico/scratch pré-alocado, atraso de grupo conhecido, flush/reset fora do callback e limites de saída. Exigir testes de seno/impulso, aliasing, continuidade, frames parciais e NaN. Nenhuma classe vazia foi criada para simular esse contrato.

Drift: comparar posições/timestamps dos dois relógios e ocupação da fila com alvo definido. Controlador futuro fornece razão de resampling limitada e suavizada, sem saltos não controlados nem mudanças de volume. Reportar drift, perdas e recuperação. Excesso/falta atuais são política de falha limitada, não correção de drift. Validar jitter, reconexão e execução prolongada antes de monitoramento em produção.

## Auditoria da Alpha e NAudio 3.1.0

| Componente inspecionado | Ownership e restrições preservados |
| --- | --- |
| MicrophoneTestSession/Engine | Worker MTA serializa controle, geração/cancelamento e snapshots; captura curta e playback explícitos, mutuamente exclusivos. Não é o motor contínuo |
| BoundedTestAudio | Clip em memória até 10 s / 8 MiB, PCM16 ou float32, mono/estéreo e 8–96 kHz. Lock curto no append e cópia/atenuação para playback; não é a fila SPSC nova e não foi substituído |
| CoreAudioMicrophoneTestBackend | Possui MMDevice selecionado; recorder/player possuem seus clientes/threads. Formatos e mute consultados, sem setters. Construção/comandos/descarte no worker proprietário |
| AudioWatchWorker | Possui fontes/inscrição de pico na thread MTA, seleção/revisão e frescor. Leitura de amplitude, sem acesso a frames PCM; não alimenta o novo pipeline |

Fonte da versão fixada conferida: [WasapiRecorder](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/WasapiRecorder.cs) e [WasapiPlayer](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Wasapi/WasapiPlayer.cs). DataAvailable empresta ReadOnlySpan<byte> apenas durante a chamada. O recorder pode ampliar seu buffer interno de silêncio; não presumir alocação zero de toda a biblioteca. Stop da captura é solicitação; o wrapper atual evita a corrida de Starting→Capturing e descarta após conclusão. Dispose/Stop podem aguardar threads, por isso não pertencem ao callback/UI. O player compartilhado usa AutoConvertPcm/SrcDefaultQuality, mas conversão implícita do Windows não comprova drift, formatos universais ou baixa latência. MMDevice continua pertencendo ao wrapper Wave Studio e é descartado depois do stream.

Não há novas dependências, APIs privadas ou modificação no diagnóstico Alpha. A estratégia global permanece: motor de usuário + integração virtual assinada/documentada, condicionada à prova real da Fase 3. Loopback isolado não substitui o sinal ouvido; entregar microfone processado a OBS/Discord exige endpoint/integracão equivalente validada.

## Medições sintéticas desta máquina

200.000 blocos × 256 frames, estéreo, 48 kHz: 51.200.000 frames por execução. Aquecimento de 10.000 blocos antes da medição, PCM16→float32→fila→proteção neutra, consumo alternado à produção. Coleção de teste sem paralelização com as demais coleções. Conversão/arrays/instância preparados antes; serialização do relatório e I/O somente depois do trecho medido. Sem áudio, COM ou dispositivo físico.

| Métrica | Debug | Release |
| --- | --- | --- |
| Tempo de parede | 2.590,9652 ms | 1.152,6465 ms |
| CPU do processo no intervalo | 3.156,25 ms | 2.187,50 ms |
| Throughput de frames estéreo | 19,761 milhões/s | 44,420 milhões/s |
| Tempo médio por bloco | 0,012955 ms | 0,005763 ms |
| Bytes alocados na thread medida | 0 | 0 |

CPU é soma das threads do processo de testes durante o intervalo, podendo incluir runner/JIT/runtime; não é custo exclusivo do motor e pode superar o tempo de parede. Throughput não é compromisso de deadline nem garantia de uso durante jogos. Alocação zero foi observada no trecho estável desta execução, não no runtime inteiro, inicialização ou tratamento de erro. Relatórios locais em artifacts/engine-foundation/metrics-Debug.json e metrics-Release.json, ignorados pelo Git.

Fila teórica: frames enfileirados / taxa. Exemplo testado: 480 frames / 48 kHz = 10 ms; capacidade 1.024 / 48 kHz = 21,333 ms. O intervalo máximo permitido pela configuração genérica é 65.536 / 8 kHz = 8.192 ms; o backend futuro deverá impor orçamento apropriado, não selecionar esse extremo para monitoramento. Tempo de conversão/processamento e ocupação NÃO são latência acústica. Faltam períodos do SO/driver, filas dos dispositivos, resampling, USB/Bluetooth e medição física de ida/volta.

## Saída e próximos passos

283 testes aprovados em Debug/Release (252 anteriores + 31 novos), builds sem erros/avisos e restore locked auditado. Stress SPSC limitado a 100.000 frames por cenário mono/estéreo, cursores comuns e próximos do rollover, preservando ordem e limites. Um primeiro runner completo ficou incompleto e foi encerrado somente no processo próprio; causa não determinada. Os novos testes passaram isoladamente e a repetição completa limitada aprovou 283; sem falha crítica reproduzida da base.

Próximo incremento proposto, sujeito a revisão e autorização: contrato/backend WASAPI real em worker próprio, negociação comprovada e resampling/drift progressivos; teste manual com headset, nível baixo, monitoramento opt-in desligado inicialmente e parada/recuperação. Antes de integrar pipeline contínuo ou substituir fluxos Alpha, obter regressão física/validação da base. Não houve republicação da prévia aberta pelo proprietário, UI nova, alteração de volume/padrão, EQ, driver, instalador ou release pública nesta etapa. Parar na F2-I1; a Fase 3 continua condicionando expansão de efeitos.
