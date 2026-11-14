# F2-I4 — metas e contrato do resampling adaptativo

Metas registradas em 14/11/2026 antes da medição do novo componente. Proprietário serial, float32 intercalado mono/LR e políticas explícitas do ChannelAdapter. Taxas 48→48, 44,1→48 e 48→44,1 kHz; razão saída/entrada = nominal × (1 + ppm/10⁶), faixa **±500 ppm**, outros pares/razões recusados. O caminho nominal anterior conserva identidade exata e 96→48; o adaptativo sempre usa FIR, inclusive em zero ppm.

## Projeto e critérios prévios

- FIR causal de 161 taps, raio/atraso 80 frames de entrada; banco de 257 posições (256 intervalos e extremidade de fase 1), sinc/Blackman e normalização DC por fase. Interpolação linear de coeficientes adjacentes, não interpolação linear de amostras sem anti-alias. Cutoff = 0,45 × min(1,ratioNominal×0,9995) em ciclos/entrada, adequado ao pior downsample declarado. Projeto fora do processamento.
- Cursor inteiro + fração Q48, passo inverso da razão arredondado; carry entre frames/blocos, sem posição absoluta acumulada em double. Slew máximo 25 ppm por segundo nominal de saída, fase/histórico preservados a cada comando. Geração acompanha comandos/operações, reset somente explícito com geração nova. Construção/reset podem receber ppm inicial explícito; padrão zero.
- Banda útil até 0,40 × menor taxa efetiva: erro de ganho ≤0,1 dB e ripple ≤0,2 dB; grade de 16 senos, amplitude 0,25, DC e transientes excluídos. DC em regime: erro absoluto unitário ≤1e-5.
- Stopband onde há entrada válida entre menor Nyquist de saída e Fin/2: alias residual RMS pelo menos 60 dB abaixo da entrada, 16 frequências. Imagem medida no upsample 44,1→48 (entrada 16 kHz), sem usar entrada acima de Nyquist. Medir nos extremos −500/+500 e no centro; casos intermediários e fronteiras de fase entram nas verificações de continuidade/transição. Grades não provam pior caso espectral contínuo.
- Transições: comparar a senoide com a referência analítica no tempo de entrada do cursor variável (incluindo atraso). Tons 1/8/16 kHz, amplitude 0,25, comandos invertendo alvos e atravessando zero. Excluir transientes de borda; erro RMS relativo ≤−70 dB e erro absoluto de pico ≤0,0002. O resíduo inclui aproximação FIR, interpolação e quantização. A modulação prevista pela própria fase variável é sinal legítimo e não é chamada de distorção. Metas são conservadoras em relação à banda útil proposta; nenhum teste promete audibilidade ou qualidade universal.
- Blocos de até 4.096 frames de entrada e 8.192 de saída, histórico limitado; saída de um frame e blocos irregulares devem preservar a sequência. Mapear/sanitizar somente frames aceitos, contadores separados por estágio, sem trabalho proporcional ao sufixo reoferecido. Não finitos antes do histórico; proteção final e saturação representável contadas. Sem alocação recorrente, I/O, COM ou locks no processamento válido.
- EOF: congelar a razão efetiva atual, impedir novos comandos, preservar cursor e drenar zeros até a posição de entrada N+160 (N>0). Total depende da trajetória de fase, não da fórmula racional nominal; entrada vazia completa sem cauda. Padding não conta como entrada real. Reset limpa fase/contadores/EOF e exige geração nova. Sufixo de saída sempre zerado.
- Prova com amostras: controlador/estimador da geração ativa → adaptador/FIR → SPSC → memória. Comparar ligado/desligado com sementes e clocks iguais, média temporal de ocupação, fronteiras e perdas explícitas. Ensaios de amostras de 48→48 ±300 ppm e 44,1↔48 representativos por duração viável, medindo convergência; modelo de contagem de 900 s anterior continua na regressão. Falha ReprimeRequired para a trajetória; recuperação tem reset/priming/descartes explícitos. Sem gravação de arquivo de áudio, backend, dispositivo ou UI novos.

Prova de amostras planejada: 100 s simulados em cada 48→48 ±300 ppm (suficientes para atravessar orçamento sem controle e medir convergência); 35 s por 44,1↔48 ±100 ppm, mantendo os 900 s de contagem em regressão. Esse recorte limita o custo do FIR em Debug, sem substituir amostras por contagem; não há afirmação de estabilidade física prolongada. Valores de tempo simulado serão separados de parede.

Uma meta não atendida exige correção ou limitação explícita; não relaxar critérios silenciosamente. Alpha A01–A12, causa histórica do runner e Windows 11 permanecem pendentes; Windows 10 experimental.

## Contrato implementado

`AdaptiveResampler` é interno e serial. Process/Flush recebem geração; geração obsoleta ou Stop retorna zero e limpa o destino, sem consumir/mapear nem alterar fase/contadores. SetTargetPpm rejeita não finitos/faixa inválida antes de mutação e recusa comando obsoleto/parado. Reset exige geração estritamente maior e limpa histórico, fase, EOF, contadores e estado de parada; não existe reinicialização silenciosa. O proprietário da sessão associa estimador/controlador à mesma geração e deve parar ambos os estágios quando ReprimeRequired, sem continuar entregando a última razão limitada. O teste de recuperação comprova esse interlock; não é backend físico.

O banco tem 256 intervalos, incluindo coeficientes em fase 1. Para cursor n+f, a saída usa x[n−tap] × h(tap−80+f), interpolando dois conjuntos adjacentes; a extremidade de fase 1 corresponde à de fase 0 com deslocamento do índice. Histórico independente por canal. O passo é round(2⁴⁸/ratio), somado a fração unsigned com carry para índice inteiro checked. Apenas a fração vira double; o índice absoluto não é acumulado em double. Um milhão de avanços com índice inicial acima de 2⁵³ é comparado a UInt128, inclusive overflow rejeitado sem mutação parcial. Erro de quantização do passo ≤0,5 unidade Q48 por saída, cerca de 7,4e-6 frame/dia a 48 kHz; não se afirma um dia inteiro de processamento físico.

Process aceita um prefixo e retorna contadores em taxas diferentes. Mapeia um frame somente quando o FIR realmente precisa aceitá-lo. Saída sem espaço consome zero; saída de um frame, entrada vazia para drenar saídas já prontas e repetição do sufixo são suportadas. Sobras do destino ficam zeradas. Trabalho por chamada limitado aos blocos declarados; passo abaixo de dois frames de entrada por saída neste subconjunto. EOF é explícito somente após fornecer toda entrada real. BeginFlush congela a razão corrente naquele ponto da trajetória, não a nominal nem um alvo ainda não atingido. Para equivalência de fragmentação com slew, o chamador deve aplicar comandos/EOF na mesma posição de saída e drenar as saídas já prontas com Process de entrada vazia antes de congelar. Não se promete equivalência entre comandos colocados em fases diferentes.

Flush usa padding zero e termina quando o cursor atinge N+160, sem contá-lo como entrada real. RemainingFlushFrames calcula a distância Q48 restante dividida pelo passo congelado com UInt128 e teto, não reutiliza ceil((N+160)×taxaNominal). Chamadas após completo retornam zero; Process e comandos após EOF são recusados até reset. Cauda não gerada/histórico descartado em uma interrupção não viram frames falsos da fila.

Contadores separados: não finitos de entrada antes de duplicação/downmix, não finitos na proteção de saída, saturação representável do acumulador e clipping final. Um sufixo reoferecido ainda não aceito não aumenta MappedFrames ou sanitização; padding não é mapeado. Saturação representável e clipping podem referir-se à mesma amostra em etapas distintas, portanto não são somados como quantidade de amostras únicas. O pipeline posterior pode registrar zero clipping porque o adaptativo já protegeu: isso não apaga os contadores anteriores. Ganhos/padrões/volumes do Windows não são alterados.

Arrays por instância: banco 165.508 bytes, histórico estéreo 2.048 e frame mapeado 8; payload máximo 167.564 bytes, além dos objetos fixos. Com SPSC de 2.048 e scratch de 512 frames estéreo: 188.044 bytes. Coeficiente/índice circular é calculado uma vez por tap e compartilhado por canais; somas/históricos seguem independentes. Essa otimização reduziu custo Debug, sem mudar ordem de soma por canal ou o hash do ensaio de equivalência. Nenhum código do NominalResampler/NominalConversionStage/ChannelAdapter foi alterado.

## Qualidade medida

Nove configurações estáticas (três pares × −500/0/+500 ppm). Grade útil de 16 senos e DC, duração Fin/8, exclusão de 512 saídas em cada borda. Ajuste seno/cosseno por mínimos quadrados na taxa efetiva de saída (Fout×(1+ppm/10⁶)), sem pressupor ciclos inteiros. DC usa 0,25 e mede ganho relativo unitário. Alias inclui todo o RMS residual e imagem remove a fundamental antes do ajuste da frequência dobrada. Fase inicial dos tons é 0,37 rad. Resultados determinísticos iguais em Debug/Release, sem teste auditivo.

| Grupo | Maior erro útil dB | Maior ripple dB | Pior alias dB | Pior imagem medida dB |
| --- | --- | --- | --- | --- |
| 48→48 | 0,00027030 | 0,00027031 | -86,14299 (−500 ppm) | não medida neste grupo |
| 44,1→48 | 0,00021409 | 0,00024117 | não há entrada acima do Nyquist de saída | -123,90210 |
| 48→44,1 | 0,00026359 | 0,00026359 | -90,65794 | não medida neste grupo |

Maior erro DC: 5,96046e-8. Todas as metas foram atendidas na grade; não é limite universal em frequência contínua. Imagens próximas da fundamental em 48→48 com desvio positivo não foram medidas separadamente; não fabricar um resultado. Impulsos L/R verificam isolamento e pico de atraso dentro de um frame do previsto. Atraso algorítmico 1,666667 ms nas entradas de 48 kHz e 1,814059 ms em 44,1 kHz; adaptativo não tem caminho sem FIR/atraso. O nominal anterior continua tendo identidade exata separada.

Transições de três segundos, −10↔+10 ppm com passagem por zero e três cruzamentos da fronteira do banco em cada tom:

| Tom nominal | Resíduo RMS relativo dB | Erro absoluto de pico |
| --- | --- | --- |
| 1 kHz | -139,63343 | 5,18399e-8 |
| 8 kHz | -115,56262 | 5,69220e-7 |
| 16 kHz | -103,20046 | 2,30580e-6 |

Referência analítica usa a posição Q48 antes de cada saída menos os 80 frames de atraso. As metas −70 dB/0,0002 foram atendidas sem retirar a modulação prevista da trajetória. Extremos de ±500 são medidos estaticamente; a transição dinâmica não é uma varredura exaustiva de todos os sinais/ppms. Blocos irregulares/saída de um frame, reset, EOF, zero-space, parâmetros inválidos, geração antiga, proteção e parada têm testes próprios.

## Prova sintética de amostras

Gerador de senos em memória → ChannelAdapter → FIR adaptativo → SyntheticAudioPipeline/SPSC original → memória, com hash de todos os frames aceitos comparado ao de todos os lidos após drenar a fila. Saída finita não nula verificada, não apenas contagem. O gerador usa frequências por segundo nominal de entrada, sem alegar pitch físico calibrado. Input mono→LR nos casos de entrada 48 kHz; estéreo→mono em 44,1→48. Clocks, jitter e sementes são os mesmos em cada par ligado/desligado; observações de clock não são confundidas com posições já entregues pelo callback.

| Taxas / diferença | Duração simulada por caminho | Ocupação min/max com controle | Convergência | Erro final ppm | Perdas com/sem controle |
| --- | --- | --- | --- | --- | --- |
| 48→48 / −300 ppm | 100 s | 687 / 1.304 | 71,7 s | -11,02474 | 0 / 656 underruns |
| 48→48 / +300 ppm | 100 s | 743 / 1.364 | 73,3 s | 11,58647 | 0 / 656 drops |
| 44,1→48 / +100 ppm | 35 s | 782 / 1.280 | 24,0 s | -7,59269 | 0 / 0 |
| 48→44,1 / −100 ppm | 35 s | 790 / 1.247 | 23,9 s | 9,10589 | 0 / 0 |

Convergência: 20 s contínuos em Tracking com erro de razão efetiva ≤30 ppm e ocupação filtrada ≤64 frames do alvo, usando a razão realmente aplicada pelo resampler. A média temporal nos últimos cinco segundos ficou entre 992,18 e 1.055,23 frames. Nos caminhos curtos sem controle o orçamento ainda não foi esgotado, mas a ocupação se afasta mais do alvo; os 900 s do modelo de contagem anterior continuam na regressão e cruzam o orçamento. São 540 s simulados no total dos oito caminhos, sem afirmar igual duração de teste físico.

Priming de 1.024 zeros explícito; soma gerados+priming = lidos+drop+fila final. Underrun é pedido não atendido, não frame lido; hash exclui zeros artificiais de underrun e inclui priming. Drenagem final é medida separadamente da leitura durante a simulação; não é novo período de captura. Não houve clipping/sanitização indevidos nesses sinais. Fila-alvo a 48 kHz = 21,333 ms e capacidade 42,667 ms; não é latência acústica.

Na prova de descontinuidade, ReprimeRequired chama Stop nos dois estágios antes de qualquer nova entrega, mantém 256 frames antigos explicitamente descartados no reset, cria geração 2 e estimador/controlador novos/limpos; comandos e observações da geração 1 são recusados. Amostras novas voltam a ser produzidas/lidas após reset. O histórico/cauda não materializada também é invalidado, sem chamá-lo de drop de frames já produzidos. Não há recuperação automática ocultando perdas ou autorização para revogar dados já submetidos ao SO.

## Estabilidade e verificações

Foi reproduzida a rejeição cumulativa do estimador: a 8 kHz, deltas curtos de 805 frames/100 ms cabem na tolerância local, mas 8.050 frames/1 s excedem a janela longa. A amostra rejeitada agora não avança nenhuma baseline; Latest é invalidado e RequiresReset permanece até reset/descontinuidade explícita. Testes cobrem caso com/sem estimativa anterior e retomada. IDs de clocks continuam ligados a cada instância do estimador; ClockEstimate não carrega identidade por si só, portanto o proprietário deve refazê-lo/limpá-lo e não reaproveitá-lo ao trocar geração.

Um runner Debug intermediário encerrou por exceção não tratada após 342 casos aprovados: WindowPlacement.Fit recebeu área útil inválida em inicialização de MainForm. Correção mínima de produção: ignorar monitores com dimensão inválida; se não houver nenhum válido, conservar bounds/posição salva e informar indisponibilidade, sem gravar posição substituta no fechamento. Sete testes STA cobrem lista vazia, inválida e mista, coordenadas negativas, dimensão pequena, monitor removido e preferência fora da tela/maximizada, com seleções habilitadas e valores salvos preservados. O complemento remoto bd0ab52 foi incorporado sem perder trabalho. A API estrita WindowPlacement.Fit continua validando sua entrada. Sem redesign ou mudança em áudio.

Outro Release completou os 371 casos com 370 aprovados e um timeout no caso histórico de duas aberturas. Log chegou ao Disposed da segunda, sem aviso de armazenamento e sem confirmar o retorno de DoEvents. O fixture de preferências foi convertido para um Application.Run próprio com ações/esperas assíncronas, preservando prazos, asserções e coleta de avisos. É uma correção do mecanismo de teste reentrante, não comprovação de causa de todos os bloqueios históricos. Não remover avisos da produção ou tratar uma repetição aprovada como diagnóstico definitivo.

Na ampliação de cobertura após bd0ab52, uma tentativa de 13 casos teve seis aprovados/sete falhos por verificar painel habilitado antes de terminar a consulta assíncrona. A espera passou a exigir simultaneamente checkboxes e painéis habilitados, mantendo a asserção e o prazo. Os 13 passaram isolados, seguidos de 375 nas suítes completas finais. A prévia da pasta habitual permanece preservada; o complemento exige autorização específica e fechamento normal para substituir o pacote, ações que não foram inferidas da autorização de implementação/commit/push.

Watchdog: orçamento total inclui prazo do processo, término e coleta de pipes. ReadToEnd só é consultado quando concluído; coleta pendente/falha gera saída incompleta e código 125, em vez de espera indefinida. Descendente exige nonce único desta invocação, parentesco/criação reconferidos após abrir/pinar o handle e recursão limitada; processo que já encerrou é tratado. Identidade ambígua é recusada. Probe local C# compilado sob artifacts, sem pacote novo, executa dois cenários inofensivos: timeout do pai+filho e pai encerrado com filho segurando stdout. Filho confirmado encerrado em ambos; coleta completa. Códigos 124/125 são esperados nesses ensaios. Conhost.exe sem nonce foi recusado, não morto; essa limitação é explícita. Política de execução/segurança preservada, sem instalação/elevação do Windows. Logs/tempos e regressões finais no status. Sem stack dump ou arquivo de áudio persistido.

## Custo medido e reprodução

Trecho aquecido: 8.192 frames mono a 44,1 kHz → estéreo 48, alvo +500 ppm com slew ativo, incluindo Process, hash numérico e Flush. Arrays/projeto de coeficientes/geração do sinal ficam antes da janela. Ambos os tamanhos de destino produziram 9.091 frames, hash 22140796100654659, e mapearam exatamente 8.192 frames. Não se mediu só um caminho estático de passo em cache.

| Métrica | Debug, saída 512 | Debug, saída 1 | Release, saída 512 | Release, saída 1 |
| --- | --- | --- | --- | --- |
| Parede ms | 12,2518 | 13,2498 | 2,5900 | 6,5401 |
| Frames de saída/s | 742.013,42 | 686.123,56 | 3.510.038,61 | 1.390.039,91 |
| Bytes alocados na thread medida | 0 | 0 | 0 | 0 |

O custo não cresce com remapeamento repetido do sufixo: ambas as capacidades mapearam somente o aceito. O nominal anterior continua inalterado e conserva seu custo conhecido de remapeamento parcial. Zero alocação refere-se ao trecho válido após aquecimento, não à construção/erros/runner inteiro. Throughput varia com JIT/runner/carga e inclui hash; não é garantia de deadline ou CPU durante jogos.

Integração completa com controlador/SPSC/geração/hashes: os oito caminhos de 540 s simulados consumiram, somados, cerca de 35,64 s de parede Debug e 12,17 s Release nas suítes finais; relatórios individuais registram parede. Esses números não são latência física nem devem ser convertidos diretamente em porcentagem de CPU. Ensaio isolado Debug antes da otimização de coeficientes/índices levou 98,708 s para os cinco casos; o compartilhamento de trabalho estéreo reduziu o custo, mantendo hash e metas. Não há asserção de benchmark de parede.

Restore locked auditado e builds Debug/Release x64 sem erros/avisos. Suítes finais após revisão do fixture STA: **375 aprovados por configuração**, zero falhas/ignorados; Debug 50,628 s e Release 23,694 s. As falhas intermediárias descritas acima não foram ocultadas. Comando completo de diagnóstico, após build:

```powershell
& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 scripts/Test-WithWatchdog.ps1))) -RepositoryRoot (Get-Location).Path -Configuration Release -Seed 20261113 -TimeoutSeconds 180
```

Métricas em artifacts/adaptive-resampling/*.json; logs do runner/probe em artifacts/clock-drift. Diretórios ignorados pelo Git. Prazo total do watchdog 180 s nos ensaios completos, 165 s internos MTP e reserva de encerramento/coleta; provas próprias usam 30 s. Não foi instalada ferramenta, alterada ExecutionPolicy ou iniciada sessão física de áudio. Executável publicado preservado.

## Próximo contrato — F2-I5 proposta, não iniciada

Backend WASAPI isolado em worker proprietário, somente mediante revisão/autorização e regressão Alpha: consultar capacidades reais, negociar formato/máscara/períodos/latência, identificar direção/endpoint/geração e qualidade de clock, converter unidades nativas para frames e QPC correlacionado em referência comum conforme APIs públicas. [IAudioClock.GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition) deve ser interpretado com [GetFrequency](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getfrequency); timestamp correlacionado usa unidades de 100 ns e não é diretamente Stopwatch.GetTimestamp. Não presumir que o wrapper atual já entregue esse contrato.

Parar/desregistrar callbacks, aguardar quiescência e revalidar ID/geração antes de submeter. ReprimeRequired impede entrega; renegociação/reconexão cria nova geração e priming explícito. Interlock com diagnóstico de microfone: captura curta/reprodução de teste e monitoramento são mutuamente exclusivos; monitoramento opt-in desligado inicialmente, com headset/nível baixo e saída confirmada. Nenhum fallback de endpoint, gravação, ajuste de volume/padrão ou stream automático. NaN/proteção/perdas permanecem por estágio. Não integrar à Alpha ou iniciar áudio físico enquanto revisão e regressão A01–A12 estiverem pendentes; Windows 11 segue sem ensaio, Windows 10 experimental. Fase 3/prova externa continua condicionando efeitos globais.
