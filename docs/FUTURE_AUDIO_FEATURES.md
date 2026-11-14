# Wave Studio — referências e evolução de áudio

Registro de propostas do proprietário, 12/11/2026. Complementa o planejamento, sem substituir [REQUIREMENTS.md](REQUIREMENTS.md). Recursos abaixo são objetivos futuros ou estudos de viabilidade, não funcionalidades implementadas. Respeitar as fases e a prova de integração global antes de ampliar DSP.

## Equalização — referências Equalizer APO e Peace
- Modos simples (graves/médios/agudos), gráfico (10 bandas previstas) e paramétrico avançado com quantidade de bandas ajustável, frequência, ganho, Q, tipos de filtro, preamp, bypass, seleção de canais, curva de resposta real e proteção contra clipping.
- Presets editáveis/duplicáveis, importação/exportação JSON versionado, avaliar importação de subconjunto documentado de configurações de Equalizer APO (texto e filtros suportados; avisar sobre comandos não suportados, nunca executar comandos/scripts ou includes arbitrários). Não alegar compatibilidade integral de Peace sem teste.
- Avaliar AutoEQ e fontes de presets com licença e procedência verificadas. Conversão e testes de equivalência de resposta. Não tornar Equalizer APO dependência obrigatória nem sobrescrever configuração existente.
- Referência de interface e de organização dos parâmetros, não cópia de marca ou código.

## Cadeia de microfone — referências Light Host e plugins
O proprietário deseja reunir os tratamentos que usa com plugins de terceiros. Prioridade: efeitos nativos no Wave Studio, cadeia ordenável por usuário, bypass individual/global, presets, medição de entrada/saída e redução de ganho, parâmetros com limites seguros, suavização, proteção de picos, latência medida, processamento local.
- Saturação inspirada em Saturation Knob: intensidade, modos neutro/ênfase harmônica por faixa quando implementados, ganho/mix e comparação de nível. Não confundir saturação com ganho físico.
- Supressão de ruído inspirada em ERA Noise Remover: intensidade, foco quando viável, redução de ruído constante e avaliação da preservação vocal. RNNoise/WebRTC ou algoritmo licenciado após avaliação. Gate não substitui supressão.
- De-reverb inspirado em ERA Reverb Remover: estudo de viabilidade para reduzir reverberação ambiente; não prometer remoção perfeita nem usar reverb artificial como solução.
- Compressor inspirado em ReaComp: threshold, ratio, attack, release, knee, makeup gain, detector/sidechain quando implementado, mix wet/dry, medidor de redução. Presets iniciais ajustáveis, sem parâmetros universais.
- Gate inspirado em FR-GATE: threshold, attack, hold, release, range/histerese quando justificados; limiter inspirado em FR-LIMIT: ceiling, release, medidor de redução e prevenção de clipping. Ganho digital em dB inspirado em BL Gain12, separado de ganho analógico/volume Windows. De-esser, AGC e pitch previstos nos requisitos.
- Ordem inicial sugerida (editável): ganho de entrada moderado -> supressão/de-reverb se disponível -> gate -> EQ -> compressor -> saturação opcional -> de-esser -> limiter -> saída. Efeitos desligados/ganhos neutros por padrão; limiter de segurança quando adequado. Evitar feedback/duplicação e não iniciar monitoramento sem consentimento.
- A cadeia deve operar no motor real e preservar estado, bypass, latência e limites; controles não implementados não aparecem como funcionais.

## Estudo de hospedagem de plugins externos
- Avaliar VST3 x64 como extensão futura, não pré-requisito do MVP. Verificar SDK/API/licença vigente e licenças dos plugins individuais; não redistribuir binários de terceiros sem autorização.
- Definir scanner em escopo explicitamente autorizado, isolamento de plugins potencialmente instáveis, comunicação com host, compatibilidade de formatos/canais, UI, parâmetros, estado, latência compensada, bypass e recuperação após crash. Não carregar DLL/binário desconhecido automaticamente. Avaliar C++/interop somente se justificado. Não prometer compatibilidade com plugins descontinuados, VST2, VST3 ou formatos adicionais sem prova.
- Ensaiar primeiro plugin de teste seguro e licenciado, após o motor e integração externa funcionarem. Decisão go/no-go por custo, segurança e latência.

## Sequência
- F1-I6: somente validação da Alpha, correções e executável.
- Após Fase 1: concepção de design/identidade visual e componentes reutilizáveis em paralelo ao planejamento técnico, sem telas que simulem DSP. Aparência moderna própria, tema escuro, faders, knobs e gráficos acessíveis, modo simples/avançado.
- Fase 2: motor, formato PCM, buffers, contrato de efeito modular, bypass, parâmetros e medição. Fase 3: prova externa de reprodução e captura. Fases 4–5: mixer e barramentos. Fase 6: equalização avançada, estudo de importação/AutoEQ. Fase 7: processamento vocal nativo, incluir cadeia modular e avaliar de-reverb. Fase 9: voz/pitch. Host VST3 como estudo/expansão posterior após prova e revisão de viabilidade.
- Novos itens não são bloqueadores da Fase 1 nem autorização de instalação de software/drivers.

## Adendo — teste de microfone solicitado pelo proprietário

Na prévia Alpha, o proprietário confirmou volume e mute da captura e solicitou ouvir a própria voz dentro do Wave Studio. Priorizar teste local por gravação curta em memória e reprodução explícita, sem gravação em disco e sem captura automática. Monitoramento ao vivo/sidetone digital continua na Fase 2: seleção explícita de headset, nível independente baixo, desligado por padrão, stop imediato, buffers limitados, prevenção de feedback, recuperação, medição de latência. Futuramente permitir comparar sinal original/processado. Mic mono não possui balanço L/R físico; futuro pan no mixer é distinto.

## Adendo — hub, reparos e apoio ao desenvolvedor

Rascunho do proprietário de 14/11/2026 detalhado em [PRODUCT_HUB_PLAN.md](PRODUCT_HUB_PLAN.md). Acrescentar teste de saída junto ao seletor, presets de voz robótica/monstruosa como sugestões, isolamento de fala conforme viabilidade, diagnóstico e assistente de reparos seguro e página de apoio voluntário. Não confundir diagnóstico com atualização automática de drivers; nenhuma integração de pagamento ou driver autorizada. Organização visual futura e funcionalidades reais preservadas. Não ampliar a tarefa atual.

## Estudo opcional — áudio por aba de navegador

Ver [plano mestre](PRODUCT_MASTER_PLAN.md), refinamentos da validação. API tabCapture permite acesso mediante ação explícita; investigar extensão Chromium opcional e ganho por aba após F3, sem promessa de cobertura universal. Não ampliar tarefa atual. Descartar se custo/segurança/estabilidade não justificarem.
