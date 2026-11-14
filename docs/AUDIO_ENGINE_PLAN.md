# Motor de áudio — fundação, conversão e relógios

## Adendo EQ-PREVIEW-1 — reutilização do diagnóstico curto

Prova audível em 14/11/2026, independente do backend contínuo: EQ de dez bandas em Audio/Dsp aplicado pelo provider da gravação curta existente, com snapshot imutável/estados novos por play, A/B sem alterar o original e proteção/headroom explícitos. APIs públicas dos biquads NAudio já disponíveis; nenhum pacote, FIR/clock, stream de captura ou worker novo. [Contrato e teste local](EQ_PREVIEW.md). F2-I6 permanece pendente, sem liberação de monitoramento/rota global; Alpha em validação.

## F2-I5 — implementação atual, 14/11/2026

Consulta WASAPI real somente leitura e prova de transporte simulado com a cadeia adaptativa anterior. [WASAPI_BACKEND_PLAN](WASAPI_BACKEND_PLAN.md) registra auditoria de APIs públicas NAudio 3.1.0/Microsoft, campos do formato, canais, unidades/qualidade de clocks, evidências de capacidade versus negociação efetiva, ciclo de vida, buffers e bloqueios. Sem pacote/projeto novo ou integração na Alpha.

Sessão de worker MTA, fila limitada, geração/IDs explícitos, callbacks em papéis separados, gate de entrega irreversível e quiescência antes de qualquer descarte. Stop/Reset do componente serial não competem com Process. Produtor aplica ppm; consumer só lê fila e atenua/codifica. Interlock com diagnóstico testado isoladamente, default mute/nível digital 0,2. Erros/desconexão/suspensão/cancelamento invalidam entrega, sem fallback/restart. Recuperação manual recria estimador/controlador/adaptativo/fila e contabiliza descarte/priming. Timeouts preservam recursos em uso.

Contadores por eventos integram média temporal exata **somente sob o protocolo de watermark do fake**, sem extrapolar snapshots. Drops/underruns sinalizam fronteiras reais ao controlador; ReprimeRequired e qualquer falha de clock interrompem entrega imediatamente. Prova FIFO compara bytes de saída à referência FIR, incluindo priming, mono→LR e ganho digital. Clock adapta posição/frequência/QPC por deltas inteiros/UInt128 e preserva precisão acima de 2⁵³.

Bloqueio concreto: wrapper GetPosition da tag fixada não distingue S_FALSE conforme contrato Microsoft; nenhum clock nativo ativo é declarado válido. BufferSize/StreamLatency exigem inicialização e permanecem null. Mix/IsFormatSupported não provam negociação/latência efetivas. F2-I6 apenas proposta após revisão, regressão Alpha e autorização; nenhuma nova captura/reprodução, EQ, driver ou Fase 3. Código/fluxos da Alpha e nominal anterior preservados. Resultados finais em PROJECT_STATUS/TEST_PLAN; testes físicos A01–A12/Windows 11 pendentes.

## Histórico — F2-I4, 14/11/2026

Base sincronizada de 5139c51 para e04198f, depois para bd0ab52 durante o trabalho, incorporando o complemento do incidente sem perder alterações locais. `AdaptiveResampler` processa amostras float32 de verdade, mapeia somente frames aceitos, interpola banco FIR de 257 fases e usa cursor inteiro/Q48. Mono/LR, 48→48 e 44,1↔48 kHz, ±500 ppm e slew 25 ppm/s; demais configurações recusadas. O nominal (inclusive identidade exata e 96→48) permanece intacto. Contratos, metas prévias, qualidade, integração, contadores e limites em [ADAPTIVE_RESAMPLING_QUALITY.md](ADAPTIVE_RESAMPLING_QUALITY.md).

- Comandos/operações incluem geração. Stop recusa entrada, saída e comandos; reset exige geração nova. O ensaio conecta estimador/controlador da geração ativa → adaptador/FIR → SPSC original → memória, e interrompe entrega em ReprimeRequired. Recuperação descarta fila antiga explicitamente e não reaproveita estimativa/comando anterior.
- EOF congela razão efetiva e termina pela posição Q48 na cauda, sem fórmula racional fixa. Padding separado de entrada real. Sanitização antes do histórico, saturação representável e clipping final têm contadores próprios, sem recontar sufixos oferecidos. Payload máximo do componente 167.564 bytes, com fila/scratch do ensaio 188.044; zero alocação recorrente na thread medida.
- Nove configurações de qualidade: pior erro útil 0,00027030 dB, ripple 0,00027031 dB, alias -86,14299 dB, imagem medida -123,90210 dB, erro DC 5,96046e-8. Transições contra referência de fase variável: pior resíduo -103,20046 dB, pico 2,30580e-6; metas atendidas na grade, sem promessa espectral universal.
- Quatro pares de amostras ligado/desligado: 100 s simulados em 48→48 ±300 ppm e 35 s em 44,1↔48 ±100, com 540 s simulados no conjunto. Zero perdas com controle; sem ele 656 perdas por cenário de ±300. Convergência 23,9–73,3 s, hash de todos os frames aceitos/lidos consistente e recuperação exercitada. Os 900 s de contagem anteriores continuam na regressão, sem substituir amostras por contagens.
- Corrigida rejeição cumulativa do ClockRatioEstimator: baseline curta só é confirmada após a janela aplicável ser validada; janela longa inválida invalida Latest e exige reset. Dois casos reproduzem acúmulo plausível local/implausível global e retomada.
- Durante regressão houve exceção comprovada de área útil inválida no MainForm: guardas preservam posição quando monitores estão indisponíveis, sem redesign. Sete casos STA cobrem área vazia/inválida/mista, coordenadas negativas, dimensões pequenas, monitor removido, preferência fora da tela e maximização sem área, com seleções habilitadas e valores persistidos preservados. Outra tentativa Release teve timeout após Disposed no caso histórico; fixture STA passou a usar Application.Run/esperas assíncronas, com todos os prazos/avisos/asserções preservados. Causa completa histórica não comprovada.
- Watchdog revisado limita coleta de pipes, valida nonce/parentesco/criação após pin do handle e recusa PID ambíguo. Probes próprios de timeout e pipe aberto encerraram o filho confirmado e retornaram em orçamento; conhost sem nonce foi recusado, não morto. Sem alteração de ExecutionPolicy, instalação ou elevação do Windows.

Restore locked auditado, builds Debug/Release x64 sem erros/avisos. Suítes finais: 375 aprovados por configuração, zero falhas/ignorados (336 anteriores + 39 novos; 356 regras/simulações/arquivos/STA/sintéticos + 19 integrações Windows de leitura/janelas). Debug final 50,628 s; Release final 23,694 s. Logs/relatórios locais ignorados; falhas anteriores registradas no status. Nenhum stream físico novo, UI de DSP, driver, instalador, release pública ou redesign. Executável publicado preservado. F2-I5/backend isolado é proposta condicionada a revisão/autorização e regressão Alpha; parar após F2-I4.

## Histórico F2-I3 — 14/11/2026

Base sincronizada de c206d8c para e9c045f. Dois arquivos de produção internos, AudioClock.cs e ClockRatioController.cs, acrescentam descritor/observações, estimador de razão e controle limitado. Simulação determinística usada de verdade por ClockDriftTests, com eventos independentes, contabilidade de frames e fila limitada. Nenhuma integração com amostras, FIR nominal, UI ou hardware. [Contratos, metas prévias, método e resultados](CLOCK_DRIFT_SIMULATION.md).

- Estimativa por deltas de posição/tempo em referência comum; IDs/gerações, unidades, qualidade, idade, regressão, saltos e descontinuidade verificados. Janela ≥1 s, filtro alpha 0,2. Controle a cada 100 ms com feed-forward + PI de ocupação média temporal, limites ±500 ppm e slew 25 ppm/s. Falha persistente requer reset/repriming explícito; sem dados suficientes mantém nominal. Posição nativa desconhecida não é presumida como frame.
- Dezoito pares ligados/desligados de 900 s: 48→48, 44,1→48 e 48→44,1, ±50/100/300 ppm, jitter e offsets. Zero perdas com controle; sem ele, 1.181–12.176 frames perdidos. Convergência 21,8–76,7 s; maior erro final 3,006 ppm. Fora de orçamento e timestamps inválidos geram falha/contadores; pausa/geração nova recuperam sem apagar perdas anteriores. São simulações de contagem, não resampling adaptativo de áudio ou relógios físicos sincronizados.
- Corrigida amostragem da ocupação no simulador: a leitura instantânea a cada 100 ms aliava os blocos de 5 ms. Média ponderada pelo tempo remove esse erro de observação, mantendo os ganhos e metas originais. Backend futuro terá de cumprir esse contrato, inclusive informar falhas reais de fronteira do intervalo.
- Medido custo da etapa nominal com consumo parcial; destino de um frame provocou ~493 vezes mais frames remapeados que destino de 512, sem diferença de amostras/contagem. FIR/contrato nominal preservados. Planejado prefixo necessário/cache com ownership e contadores de proteção por estágio, sem contabilizar sufixo reoferecido como nova entrada aceita.
- Teste STA de preferências recebe coletor de avisos com asserções, logs de ciclo de vida, prazo fora de DoEvents e Join. Watchdog externo restringe eventual encerramento à árvore recém-criada do runner. Nenhum bloqueio reproduzido nesta etapa; causa modal permanece hipótese, sem alteração de produção. Revisão e regressão física continuam bloqueando integração.

Restore locked auditado e builds Debug/Release x64 sem erros/avisos. 336 testes por configuração (313 anteriores + 23 novos: 324 regras/simulações/arquivos/STA/sintéticos e 12 integrações Windows existentes), todos aprovados nas execuções finais. Repetições limitadas e sementes no status. Nenhum teste novo abriu stream físico. Prévia publicada/identidade/áudio da Alpha preservados. F2-I4 proposto: aplicar razão variável a amostras em resampler validado, antes de backend WASAPI. Parar após F2-I3; sem EQ, Fase 3, driver ou redesign.

## Histórico F2-I2 — 13/11/2026

Base sincronizada de b875bec para 2e286cf. `ChannelAdapter`, `NominalResampler` e `NominalConversionStage` são internos, independentes da UI e realmente exercitados com sinais sintéticos. A trajetória testada é `float32 → canais → FIR nominal → proteção → SyntheticAudioPipeline/SPSC na taxa de saída → memória`. O pipeline original continua exigindo formatos iguais em suas duas extremidades; a conversão ocorre antes dele, sem alterar os fluxos Alpha.

### Canais e segurança numérica

Layouts aceitos: mono lógico e estéreo frontal lógico L/R. A política é obrigatória: Preserve conserva o layout; DuplicateMono duplica uma amostra nos dois canais com coeficiente 1 por canal; AverageStereo calcula 0,5L + 0,5R. A duplicação não cria informação estéreo, e a média cancela sinais em anti-fase. Não há boost no downmix; somar os termos já atenuados evita overflow intermediário de L+R. Layout desconhecido, multicanal, política incompatível, frame parcial, sobreposição ou bloco acima de 4.096 frames são recusados. A quantidade de canais não autoriza presumir posições de um futuro endpoint: seu backend deverá validar máscara/ordem antes de construir o plano lógico.

NaN/Infinity viram zero antes de entrar no histórico do FIR. Amostras finitas acima de 1 ainda podem existir nos estágios intermediários; acumulador double e saturação no limite representável de float evitam infinito na saída numérica do FIR. A proteção final independente limita a [-1,1]. Esse hard clipping pode distorcer; não é compressor, ganho do endpoint nem limiter com lookahead. O FIR também pode apresentar overshoot, portanto a proteção continua necessária mesmo com entrada nominal.

### Resampling streaming e contabilidade

- Taxas em Hz: identidade em 44.100, 48.000 e 96.000; conversões 44.100→48.000, 48.000→44.100 e 96.000→48.000. Outras combinações são explicitamente rejeitadas, sem fallback. A identidade conserva exatamente os valores finitos, inclusive zero com sinal, e não insere FIR/atraso.
- FIR causal de sinc janelada por Blackman, 161 taps por fase, raio/atraso de 80 frames de entrada. Cutoff = 0,45 × min(Fin,Fout) em Hz; em ciclos por frame de entrada, 0,45 × min(1,Fout/Fin). Janela centrada contínua: 0,42 + 0,5 cos(πd/80) + 0,08 cos(2πd/80), nula fora de |d|≤80. Kernel = 2fc sinc(2fc d) × janela, com d = tap−80+fase/up. Cada fase é normalizada para soma unitária e armazenada em float após projeto em double na construção.
- Razão reduzida up/down = Fout/Fin. O cursor avança por divisão/resto inteiros de fase+down; fase permanece entre 0 e up−1. Não se acumula erro de fase de ponto flutuante. A soma por canal usa seu próprio histórico, sem mistura L/R. Atrasos: 1,814059 ms em 44,1→48, 1,666667 ms em 48→44,1 e 0,833333 ms em 96→48. São atrasos algorítmicos, não latência acústica.
- `Process` aceita até 4.096 frames de entrada e espaço para até 8.192 frames de saída. Retorna ConsumedFrames na taxa de entrada e ProducedFrames na taxa de saída. Pode consumir apenas um prefixo; o chamador conserva e reapresenta o sufixo. Espaço de saída zero consome zero. Há suporte a saída de um frame, entrada vazia para drenar fase pendente e blocos irregulares; o sufixo não produzido do destino fica zerado. Somente o prefixo produzido pode ser enfileirado.
- `BeginFlush` declara EOF somente depois de todos os frames reais terem sido aceitos. Para N>0 no FIR, o total com cauda é ceil((N+160)×Fout/Fin); para N=0 é zero; na identidade é N. O arredondamento usa aritmética inteira checked. `Flush` preenche o histórico com zeros sem contabilizá-los como entrada real, devolve produção parcial e sinaliza Completed ao atingir o total. Chamadas posteriores retornam zero/completo; Process após EOF é recusado. Reset limpa histórico, fase, EOF e contadores, reutilizando coeficientes e memória.
- Sem flush, a saída disponível depende da fase e do último frame aceito; não usar a fórmula de cauda para prever a quantidade por chamada. Contadores InputFrames/OutputFrames pertencem às respectivas taxas. Depois da conversão, capacidade, drop/overflow, FIFO e underrun do SPSC são expressos exclusivamente em frames de saída. O chamador de teste respeita ambos os retornos; não reenvia ao SPSC frames que ele já aceitou. No modelo de callback futuro, overflow mantém a política de descarte do sufixo mais novo.

Histórico circular: 256 frames por canal; o avanço consome só até o índice necessário antes de emitir, por isso os 161 taps permanecem disponíveis. Maior tabela: 160 fases × 161 × 4 = 103.040 bytes. Histórico estéreo: 2.048 bytes; scratch mapeado: 32.768. Maior payload da etapa: 137.856 bytes, além dos objetos fixos. No ensaio com fila de 1.024 e scratch de 512 frames estéreo, total = 150.144 bytes. Sem alocação recorrente, COM, locks, threads próprias ou I/O no processamento válido; exceções de contrato não fazem parte desse regime. Construção/projeto de coeficientes ficam fora de callbacks.

### Ownership e revisão do ciclo de vida F2-I1

A etapa de conversão é de um único proprietário serial: Process, BeginFlush, Flush e Reset não são concorrentes. Ela não herda os gates do pipeline. Para futura integração, parar/desregistrar e aguardar o produtor antes de resetar essa etapa; o consumidor pode continuar somente dentro do contrato da fila. Nenhum reset/Dispose é colocado no callback de produção.

Três casos de ciclo de vida exercitam 32 corridas por ação (Stop, cancelamento e Dispose: 96 cenários), com produtor/consumidor em threads próprias de teste, prazos e Join antes de reutilizar os papéis. Outros dois casos atrasam callbacks float/PCM fora da rotina produtiva, reiniciam a geração e comprovam rejeição da anterior. Testes cobrem TryReset/Dispose durante disputa e após quiescência. Não houve falha comprovada que justificasse mudar o pipeline original. Corridas de agendamento são stress limitado, não prova de todos os interleavings.

Contrato importante preservado: um Produce já em voo pode publicar/retornar frames aceitos após Stop/cancelamento ocorrer. Render zera seu destino se observar invalidação na verificação final; invalidação posterior a essa verificação ainda é possível. Retorno positivo significa trabalho aceito/realizado pela fila, nunca autorização transacional para envio físico. Totais podem incluir trabalho de uma operação posteriormente invalidada. Backend futuro precisa revalidar alvo/geração imediatamente antes do envio, parar callbacks antigos e aguardar quiescência antes de reset/reabertura. Isso não promete revogar áudio já submetido ao SO.

### Relógios e backend futuro — planejamento, sem implementação

Resampling nominal usa razão fixa e não corrige drift entre dispositivos, mesmo quando ambos anunciam 48 kHz. Não há backend contínuo, monitoramento nem substituição do teste de microfone. O descritor futuro deverá registrar: ID estável e direção; geração da abertura; formato realmente negociado (taxa, encoding, contêiner/bits válidos, block-align, máscara/ordem); período do backend em frames; capacidade e latência consultada, com fonte/incerteza; identidade do relógio; posição acumulada de frames, timestamp monotônico/QPC e frequência/unidade; qualidade/validade/descontinuidades do relógio. Distinguir estimativa, consulta ao driver e medição acústica. Spans de captura são emprestados apenas durante callback; consulta de capabilities/COM é do worker proprietário.

F2-I3 proposto, sujeito à revisão/autorização: simular dois relógios independentes com ±50/±100/±300 ppm, jitter limitado e determinístico, pausas, timestamps inválidos, reset/desconexão e retomada, antes de escolher backend isolado. Exemplo inicial de alvo a avaliar: fila de 2.048 frames a 48 kHz, ocupação-alvo 1.024 (21,333 ms), com limites de segurança; não é uma configuração já aprovada para monitoramento. Medir ocupação mínima/máxima, tendência, drops, underruns e recuperação com mesma semente. Simular duração suficiente para atravessar o orçamento sem controle; comparar com controle limitado, sem esconder perdas em contadores.

Estratégia futura: estimar razão pelos deltas válidos de posição/QPC, filtrar jitter e corrigir lentamente a razão em torno da nominal com limite inicial de ±500 ppm e slew limitado a avaliar; realimentar erro de ocupação em torno do alvo, com anti-windup e reinicialização em descontinuidade. Não alterar volumes nem descartar/duplicar frames silenciosamente para fingir sincronismo. A tabela racional atual não aceita razão variável: será necessário validar um interpolador de fases/razão adaptativa com novos ensaios espectrais, antes de implementá-lo. Áudio físico só depois de evidência Alpha A01–A12, revisão, negociação/ownership e monitoramento opt-in em headset/nível baixo. Windows 11 continua pendente; Windows 10 experimental. Fase 3 continua condicionando expansão global dos efeitos.

### Verificação F2-I2

Restore locked auditado, builds Debug/Release x64 sem erros/avisos e execuções finais com 313 testes aprovados por configuração (283 anteriores + 30 novos; 301 regras/simulações/STA/arquivos/sintéticos + 12 integrações existentes), sem falhas/ignorados nessas execuções: Debug 11,821 s e Release 9,238 s. Release final com semente xUnit 20261113; Debug diagnóstico com semente 1542588774.

O runner incompleto se repetiu em duas outras tentativas (Debug e Release), interrompidas somente nos processos próprios. No Debug interrompido, resumo de 304 aprovados, zero falhas de casos e erro de execução pelo encerramento. O log Release identificou 304 Passed e um InProgress: PreferencesUiTests.MainWindowReloadsAbsentPreferenceAcrossTwoStartsWithIsolatedStorage, teste já existente de UI; demais casos ainda não iniciados. O timeout solicitado de 90 s não encerrou efetivamente o processo, que exigiu controle externo. Não atribuir a causa ao FIR/concorrência ou considerar o timeout garantia de encerramento. Seis casos da classe passaram isolados; repetições completas finais passaram. Causa raiz/interleaving permanece pendente, sem stack dump ou correção comprovada. Diagnósticos MTP e logs numéricos/textuais em artifacts/engine-foundation/diagnostics-* e f2i2-*.txt, ignorados pelo Git. Log também mostrou acesso negado ao sentinel de telemetria do runner no sandbox; não há evidência de relação causal. Execuções finais Release/isolada usaram TESTINGPLATFORM_TELEMETRY_OPTOUT=1. Não mudaram testes antigos, Desktop, runner ou dependências; a intermitência de UI requer investigação separada antes da integração física.

Metas previamente definidas, método, resultados de qualidade/custo e limites da grade em [RESAMPLING_QUALITY.md](RESAMPLING_QUALITY.md). Nenhum teste novo abre stream físico ou altera áudio do Windows. Executável publicado e recursos oficiais preservados. Parar após F2-I2 para revisão; F2-I3 ainda não iniciado.

## Histórico — fundação F2-I1

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
