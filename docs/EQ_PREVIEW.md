# EQ-PREVIEW-1 — equalizador audível no teste de microfone

Prévia local em 14/11/2026. Reutiliza captura curta, buffer, worker e reprodução existentes. Apenas o trecho gravado pelo usuário é processado, ao clicar em **Ouvir com EQ**. Não é EQ global, monitoramento, microfone virtual ou efeito em Discord/OBS/jogos. F2-I6 nativa continua pendente; Alpha/Fase 2 não declaradas aceitas.

## Como comparar

1. Com fones e volume baixo, selecione microfone/saída em Dispositivos. Em Testar microfone, grave uma frase e pare (limite de 10 s).
2. Clique em Ouvir gravação: original sem EQ, com nível digital inicial de 20%.
3. Pare, marque Ativar EQ e ajuste uma banda, por exemplo 1 kHz em −9 dB. Clique em Ouvir com EQ. Compare novamente com o original sem gravar outra vez.
4. Use Zerar EQ e preamp para voltar às dez bandas em 0 dB/preamp 0. Desmarcar Ativar EQ faz bypass; o botão original continua disponível mesmo com ajustes de EQ.
5. Pare antes de alterar parâmetros ou o nível digital. Limpar, seleção/atualização de dispositivos, desconexão, suspensão e fechamento preservam a política de descarte existente.

EQ desligado, bandas/preamp neutros na abertura; configurações de EQ não persistidas. Válido para gravações PCM16/float32 mono/estéreo em 44,1/48 kHz. Demais taxas continuam com o teste original; EQ desabilitado com motivo. A taxa efetivamente gravada prevalece sobre metadados do catálogo. Saída compartilhada mantém a conversão de formato Windows já usada no diagnóstico.

## Implementação e segurança

GraphicEqualizerSettings copia e valida dez ganhos de −15 a +15 dB, preamp de −30 a 0 dB e ativação. Snapshot imutável entregue ao worker MTA antes do play. GraphicEqualizer é independente de WinForms/WASAPI, com cascata peaking por canal, Q fixo √2 e centros 31/62/125/250/500 Hz, 1/2/4/8/16 kHz, todos abaixo de Nyquist nas duas taxas. Filtros/estado novos a cada reprodução; nenhuma atualização de coeficiente durante o áudio.

Reaproveitadas APIs públicas `BiQuadFilter.PeakingEQ(float,float,float,float)` e `Transform(float)` do NAudio.Core 3.1.0, já fixado e licenciado MIT. Coeficientes double/estado float do próprio NAudio; não copiamos implementação nem adicionamos dependência. Conferidos fonte local da versão fixada e [BiQuadFilter oficial](https://github.com/naudio/NAudio/blob/v3.1.0/src/NAudio.Core/Dsp/BiQuadFilter.cs). Referência matemática: [Audio EQ Cookbook, W3C](https://www.w3.org/TR/audio-eq-cookbook/).

Para reduzir boosts inesperados, preamp efetivo = preamp escolhido **menos a soma dos ganhos positivos**. Essa atenuação conservadora é exibida; muitas bandas elevadas podem soar bastante mais baixas. Não é normalização perceptual ou garantia contra todo transiente. Ganho digital existente 0–100% é aplicado depois do EQ, inicialmente 20%; nenhum volume/mute/padrão Windows é alterado. Não aumente o volume físico para compensar durante a comparação.

Dados não finitos e valores de representação absurdos (|sample| >10¹²) viram zero antes dos filtros, com contagem. Áudio comum não é hard-clipped entre bandas. Proteção final após EQ e nível digital limita a [−1,+1] e conta amostras excedentes; **é hard clip, não limiter**. PCM16 recebe quantização/saturação representável. Original mantém a proteção/atenuação anterior, sem modificação de seus bytes em memória. A comparação não é automaticamente casada em loudness.

BoundedTestAudio continua com único clip selado, até 10 s/8 MiB; nenhum segundo clip processado ou arquivo é criado. Provider processa somente o span emprestado na leitura, sem alocação recorrente, rede, disco ou locks novos no caminho de áudio. Memória extra é limitada a no máximo vinte biquads/pequenos metadados; tudo é preparado no worker. EOS conserva a duração, sem acrescentar cauda IIR. Ao finalizar, os estados de filtro ficam inelegíveis para novo play; o próximo começa limpo. Descarte do clip zera o buffer original como antes.

Engine/session preservam exclusão captura/reprodução, ID/revisão/cancelamento e rechecagem antes de Start. O provider confere validade antes/depois da cópia. Dados inválidos/clip final são exibidos no snapshot durante/depois da reprodução processada. Alterações de UI ficam bloqueadas durante gravação/play/stop/operação pendente. Reset só ajusta parâmetros, não inicia áudio. A base WASAPI/FIR/clocks/watchdog não foi expandida.

## Evidências e limites

Novos testes cobrem todos os centros/±15 dB em 44,1/48 kHz (tolerância ±0,12 dB incluindo headroom), estabilidade das cascatas nos extremos, independência L/R, flat/bypass/preamp/reset, sanitização/proteção final, PCM16/float32, blocos irregulares/EOF, original preservado, estados novos entre plays, cancelamento durante abertura, seleção e fluxo fake A/B. Testes STA verificam controles/valores, congelamento e formato não suportado. Leitura processada aquecida com zero bytes alocados na thread medida; sem novo benchmark ou teste físico automático.

Um teste antigo de UI observava Play habilitado antes de terminar a reconciliação dos demais controles. Reproduzidos Clear ainda desabilitado e, ao mudar a ordem, Play ainda habilitado depois de observar Record. Corrigida somente a espera do teste para incluir o conjunto de estados esperado; asserções preservadas. Não atribuir isso à causa histórica do runner. Resultados finais/tempos/hashes e publicação no PROJECT_STATUS.

Teste audível A/B, distorção percebida, layout/DPI, troca/desconexão durante play e Windows 11 continuam pendentes do proprietário. Windows 10 experimental. Após escuta/revisão e avaliação do saldo, decidir retomada F2-I6 para resolver clock nativo/stream antes da prova externa Fase 3 ou expansão de efeitos. Não há promessa de consumo percentual de cota.
