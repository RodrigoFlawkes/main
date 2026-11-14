# F2-I2 — metas de qualidade e protocolo sintético

Metas definidas antes da medição: taxas nominais 44.100→48.000, 48.000→44.100 e 96.000→48.000 Hz, além de igualdade entre essas três taxas. Outras combinações são recusadas. Float32 intercalado mono/estéreo lógico L/R; nenhuma entrada física ou correção de drift.

- Banda útil de 0 até 0,40 × min(Fin,Fout): erro de ganho absoluto até 0,1 dB (portanto ripple pico a pico até 0,2 dB), medido com DC e senos de amplitude 0,25, fora dos transientes.
- Banda de parada a partir de 0,50 × min(Fin,Fout), apenas onde há frequências de teste válidas abaixo de Fin/2: rejeição de alias pelo menos 60 dB relativa ao RMS de entrada. Para upsample, medir também imagens espectrais na saída, sem inventar frequência de entrada acima de Nyquist.
- DC unitário em regime: erro até 1e-5; canais independentes; contagem nominal inteira exata e saída consistente entre blocos irregulares e execução de referência.
- Igualdade de taxas: caminho sem FIR/atraso, preservando amostras finitas; sanitização de não finitos e proteção final são políticas separadas.
- Atraso nominal proposto: 80 frames de entrada no FIR de 161 coeficientes por fase. Não é latência acústica.

Projeto: interpolador racional por fases, sinc passa-baixas janelada por Blackman, cutoff de 0,45 × min(Fin,Fout), normalização DC por fase. Coeficientes calculados na construção, fora do processamento. Referências matemáticas: [resampling polifásico com FIR](https://docs.scipy.org/doc/scipy/reference/generated/scipy.signal.resample_poly.html) e [janela Blackman](https://numpy.org/doc/stable/reference/generated/numpy.blackman.html). Implementação própria em C#, sem depender de NumPy/SciPy ou copiar seu código.

As metas não equivalem a qualidade universal/profissional, suporte multicanal, drift ou integração ao backend. Um resultado fora da meta exige correção do projeto ou registro explícito da limitação, sem alterar silenciosamente os critérios.

## Método e resultados — 13/11/2026

`ResamplingQualityTests` gera senos de amplitude 0,25 e fase inicial 0,37 rad, com Fin/8 frames. Descarta 512 frames de saída de cada extremidade para excluir transientes. Ajusta seno/cosseno por mínimos quadrados (matriz 2×2) no tempo real da saída, sem pressupor ciclos inteiros ou usar apenas um bin de FFT. A fase arbitrária evita sinal identicamente nulo em frequências especiais. DC usa amostras 0,25, com erro absoluto em regime registrado abaixo; o erro de ganho unitário inferido pela escala é quatro vezes esse valor (máximo 5,96048e-8), atendendo à meta 1e-5. Não houve teste físico de DC.

A grade útil tem 16 frequências igualmente espaçadas até 0,40 × min(Fin,Fout), mais DC. Ripple considera extremos de ganho dos senos e referência de 0 dB. Para cada downsample, 16 frequências começam em Fout/2 e terminam abaixo de Fin/2; a métrica é todo o RMS residual dividido pelo RMS de entrada. Para 44,1→48, mede a imagem dobrada em 19,9 kHz de uma entrada de 16 kHz, removendo a fundamental ajustada antes do ajuste da imagem. Não há frequência de entrada inválida acima de Nyquist, nem afirmação de grade exaustiva de imagens. Essas grades não provam o pior caso em toda frequência contínua, amplitude ou sinal; uma revisão futura pode ampliá-las sem relaxar os critérios.

| Taxas (kHz) | Maior erro útil (dB) | Ripple (dB) | Erro DC absoluto | Pior alias na grade (dB) | Imagem medida (dB) | Atraso (ms) |
| --- | --- | --- | --- | --- | --- | --- |
| 44,1→48 | 0,00028106 | 0,00028106 | 1,49012e-8 | não se aplica | -123,96323 | 1,814059 |
| 48→44,1 | 0,00020090 | 0,00021451 | 1,49012e-8 | -90,35690 | não se aplica | 1,666667 |
| 96→48 | 0,00148773 | 0,00148773 | 0 | -81,11294 | não se aplica | 0,833333 |

Debug e Release produziram os mesmos números de qualidade, dentro da precisão registrada. Todas as metas foram atendidas na grade medida: ganho ≤0,1 dB, ripple ≤0,2 dB, alias/imagem abaixo de -60 dB e erro DC ≤1e-5. Atraso calculado e pico de impulso testados separadamente; não representam medição de hardware.

O FIR é causal, com zero antes do início e padding/cauda no fim; difere do alinhamento zero-phase de uma função offline como resample_poly. A normalização por fase conserva DC em regime, não elimina o transiente inicial. Kernel, fases racionais, 161 taps, EOF/contagem e limites estão em [AUDIO_ENGINE_PLAN.md](AUDIO_ENGINE_PLAN.md). Nenhuma dependência nova nem implementação de terceiros foi incorporada.

## Custo e memória

Ensaio `ConversionPlusSpscHasNoSteadyStateAllocationsAndReportsCost`: mono 44.100→estéreo 48.000, blocos de 256 frames de entrada, 500 blocos de aquecimento e 2.000 medidos. Inclui adaptação, FIR, proteção final, produção/consumo na fila original; arrays e coeficientes preparados antes da janela. Produziu 557.279 frames estéreo a partir de 512.000 frames mono durante o trecho medido, sem flush nessa janela. A fase vem do aquecimento, portanto a contagem do intervalo depende dessa fase; não arredondar cada bloco isoladamente.

| Métrica | Debug | Release |
| --- | --- | --- |
| Tempo de parede | 719,3748 ms | 271,9044 ms |
| CPU do processo no intervalo | 859,375 ms | 375,000 ms |
| Frames de saída por segundo | 774.671,28 | 2.049.540,21 |
| Tempo médio por bloco | 0,3596874 ms | 0,1359522 ms |
| Bytes alocados na thread medida | 0 | 0 |
| Payload da etapa / total com fila e scratch | 137.856 / 150.144 bytes | 137.856 / 150.144 bytes |

CPU inclui threads do runtime/runner/JIT, não é custo exclusivo do motor. Throughput nesta máquina não garante deadlines sob carga nem consumo durante jogos. Zero alocação é observação do trecho válido após aquecimento, não da inicialização, exceções ou runtime inteiro. Não há locks, COM ou I/O nesse trecho; serialização numérica ocorre depois. Relatórios locais ignorados pelo Git: artifacts/engine-foundation/resampling-quality-Debug.json, resampling-quality-Release.json, resampling-performance-Debug.json e resampling-performance-Release.json. Não contêm áudio.

Reprodução, após restore/build nas duas configurações:

```powershell
dotnet test --solution WaveStudio.slnx -c Debug --no-build -- --filter-class WaveStudio.Tests.ResamplingQualityTests --timeout 90s --long-running 15
dotnet test --solution WaveStudio.slnx -c Release --no-build -- --filter-class WaveStudio.Tests.ResamplingQualityTests --timeout 90s --long-running 15
```

Suítes finais completas: 313 aprovados em cada configuração, sem falhas/ignorados nessas execuções. Outras duas tentativas ficaram incompletas em teste de UI preexistente; diagnóstico e limite do timeout documentados no plano/status, sem causa raiz comprovada. Os valores de custo acima correspondem às execuções finais; uma execução Release anterior mediu 2,70 milhões de frames/s, ilustrando a variabilidade, sem compromisso de desempenho. Não houve teste acústico, comparação auditiva, stream físico, correção de relógios ou validação Windows 11. A prévia Alpha publicada permaneceu intacta, e a aceitação A01–A12 segue pendente conforme evidências anteriores.
