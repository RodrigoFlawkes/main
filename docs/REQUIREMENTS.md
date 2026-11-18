# Wave Studio — requisitos do produto

**Status:** especificação de funcionalidades planejadas, não uma lista de recursos já implementados.
**Descrição:** Plataforma universal de gerenciamento, processamento e mixagem de áudio para Windows, com foco em desempenho, personalização e compatibilidade entre dispositivos.
**Nome comercial provisório:** Wave Studio. Executável desejado: `WaveStudio.exe`. Versão inicial: v0.1.0 Alpha. Repositório: `Wave-Universal-Studio/main`.

## 1. Objetivo e referências
Reunir equalizador de headset/microfone, mesa de som digital, mixer do Windows, tratamento vocal em tempo real, soundboard, roteamento, surround, gravação e monitoramento. Interface simples e funcional, utilizável por leigos, sem priorizar aparência. Modo simples e modo avançado. Software independente de fabricante, local e expansível.

Referências funcionais a conferir por modelo e versão antes de implementar: Redragon Zeus H510/Zeus 2, Xear Audio Center (Volume Control, Sample Rate, Equalizer, Environment Effects, 7.1 Virtual Speaker Shifter, Surround Max, SingFX, Key Shifting, Vocal Fading, Microphone Boost, Magic Voice, Microphone Echo); FIFINE AM8/AM8PROT (ganho, volume de headset, mute, monitoramento, perfis quando suportados), SC3/SC8 (faders, entradas auxiliares, game/chat, efeitos vocais, tonalidades, pads, surround e controles conforme modelo), Genie (canais, mix pessoal e de transmissão, master, perfis, EQ, bloqueio), SoundLink (supressão, ganho automático, clareza vocal, brilho, graves dinâmicos, espacialização). Outras referências: Equalizer APO/Peace, FxSound, Voicemeeter, SteelSeries Sonar, Dolby Access, DTS, Realtek Audio Console, GoXLR, Wave Link, RØDE Connect/RØDECaster, NVIDIA Broadcast, OBS, Voicemod, Soundpad, Audacity, ReaPlugs. São inspiração, não dependências obrigatórias. Não atribua todos os recursos a todos os modelos. Não copie código, marcas, firmware ou UI proprietária. Phantom power 48 V, ganho analógico, alimentação e circuitos físicos não são emuláveis por software.

## 2. Plataforma, dispositivos e identificação
- Windows 11 x64 prioritário; Windows 10 somente após validar versão, runtime e APIs; ARM64 futuro. USB, P2/P3, Bluetooth A2DP/HFP, wireless 2.4 GHz, microfones USB/analógicos/XLR via interface, caixas, interfaces, placas internas/externas, HDMI/DP, endpoints virtuais. ASIO e exclusividade são casos avançados e podem contornar processamento.
- Enumeração de endpoints de reprodução e captura; nome, ID, estado, padrão de multimídia/comunicação, canais, formato de mixagem, amostragem, bits, conexão, modelo/fabricante/driver e recursos quando expostos. Não inventar dados. Dispositivos analógicos podem aparecer como placa de som, não headset. VID/PID nunca será bloqueio.
- Seleção independente de entrada e saída, atualização ao conectar/desconectar, mudança de padrão, recuperação e preferências por endpoint. Configuração de padrão pelo sistema se não houver API pública apropriada.

## 3. Reprodução / Headset
- Volume geral do Windows e do endpoint, mute, balanço L/R, sincronizar canais, ajuste individual de canais quando suportado, ganho digital/preamp em dB, porcentagem, medidor de nível/picos, clipping, reset e limiter. Diferenciar controle do sistema e DSP. Alterações externas devem refletir na UI quando possível.
- Mixer de sessões por endpoint: nome/processo/ícone, volumes e mute individuais, atividade e medidores quando expostos, agrupamento, configurações persistentes. Balanço por sessão e destino por aplicativo somente quando suportados; EQ por aplicativo exige fluxos separados. Game/chat, ducking e roteamento detalhados adiante.
- EQ gráfico 10 bandas: 31, 62, 125, 250, 500 Hz; 1, 2, 4, 8, 16 kHz. Ganho inicialmente -15 a +15 dB, preamp, reset por banda/global, bypass, gráfico real de resposta, espectro, compensação de ganho e proteção de clipping. EQ paramétrico com frequência, ganho, Q, largura de banda, filtros peak, shelf alto/baixo, HP, LP, notch, band-pass; adicionar/remover e ativar filtros.
- Presets Flat, Bass Boost, Super Bass, Treble, Vocal, Pop, Rock, Jazz, Clássica, Eletrônica, Hip-Hop, Funk, Filmes, Podcast, FPS/passos, AAA, Mundo aberto, Comunicação, Música ao vivo, Personalizado. Sem promessas de isolar passos por simples EQ. CRUD, duplicação, importação/exportação, associação a dispositivo/app quando roteado.
- Formatos: 16/22,05/32/44,1/48/88,2/96/192 kHz; 16/24/32 bits conforme suporte. Separar formato físico, mix do Windows, motor, modo compartilhado/exclusivo e canais. Oferecer configuração nativa se não houver API suportada para alterar formato padrão. Resampling de qualidade, não afirmar que taxa maior melhora automaticamente o som.
- Mono, estéreo 2.0, 2.1, 5.1, 7.1 e espacialização. Identificar canais expostos; não confundir headset virtualizado com oito alto-falantes físicos. HRTF/binaural, downmix multicanal para estéreo, largura, intensidade e expansão estéreo (Surround Max equivalente), controle de fase. Não apresentar reverb como 7.1. Teste real de cada canal.
- Virtual Speaker Shifter: representação do ouvinte e FL, FR, C, LFE, SL, SR, BL, BR; volume/mute, ângulo, posição, distância virtual, rotação, reset e teste; alterações reais no DSP.
- Environment Effects: quarto pequeno/médio/grande, sala, estúdio, cinema, teatro, auditório, igreja/catedral, arena/estádio, banheiro, caverna, túnel, corredor, floresta, montanhas, aberto, subaquático, personalizado. Reverb/delay, reflexões iniciais, damping, tamanho, decay, intensidade e dry/wet.
- SingFX equivalente: key/pitch shifting por semitons, preservação de duração quando possível, karaokê/vocal fading/atenuação vocal com limitações explícitas.
- Outros: Bass Boost, Dynamic Bass, Virtual Bass, Treble, Audio Brightness, Vocal Clarity, loudness/normalização, smart volume/gain, compressor, limiter, expander, crossfeed, mono, inversão L/R, atraso por canal, saturação opcional. Bypass independente.

## 4. Captura / Microfone
- Nível/sensibilidade/ganho de entrada, ganho digital de saída, mute, estado, medidores/picos/clipping, canais, reset. Diferenciar ganho analógico, boost do driver e ganho digital.
- Microphone Boost: 0, +5, +10, +15, +20, +25, +30 dB, ajuste intermediário; limiter e medição antes/depois. Ganho inicial neutro.
- Saturação harmônica independente do boost: drive, entrada/saída, intensidade, dry/wet, soft clipping, oversampling se necessário; presets suave, quente, encorpado, vintage, analógico simulado, locutor, rádio, distorção proposital.
- EQ vocal gráfico/paramétrico, graves/médios/agudos, presença, nasalidade, HP/LP, inteligibilidade. Presets natural, grave/agudo, encorpado, rádio, locutor, podcast, streaming, Discord, jogos, reuniões, estúdio e personalizado.
- Voice Changer/Magic Voice: original, grave/agudo, masculina/feminina/infantil estilizada, monstro, demônio, robô, alienígena, esquilo, fantasma, rádio, telefone, megafone, narrador, locutor, metálica, subaquática, cartoon e personalizada. Pitch, semitons, formantes quando possível, intensidade, reverb, delay, chorus, distortion, ring modulation, dry/wet. Voz naturalmente mais grave é prioridade; EQ não equivale a pitch shift.
- Correção vocal: detectar fundamental, 12 tonalidades, escala cromática/maior/menor, notas permitidas, velocidade/intensidade de correção, humanização, formantes, dry/wet. Diferenciar correção de afinação de pitch shift. Não usar marca Auto-Tune como nome comercial.
- Noise Suppression local (avaliar RNNoise/WebRTC): ventilador, ar-condicionado, teclado, mouse, cooler, chiado e ruído ambiental; desligado, leve, médio, forte, automático, personalizado conforme algoritmo; preservar fala, comparar antes/depois. Não prometer remoção perfeita de ruído ou vozes concorrentes.
- Noise Gate: threshold, attack, hold, release, hysteresis, range; presets por nível de ruído. AEC com referência real do playback e alinhamento de tempo, quando viável. Diferenciar AEC de efeito artístico de eco.
- Compressor (threshold, ratio, attack, release, knee, makeup), limiter (ceiling/threshold/release/lookahead quando possível, teto inicial opcional -1 dBFS), De-Esser (frequência, threshold, largura, intensidade), AGC/Smart Gain (nível alvo, limites, velocidade, evitar amplificar silêncio); medição de redução de ganho.
- Efeitos de voz: Echo, Delay, Reverb, Chorus, Flanger, Phaser, ambientes acústicos; controle próprio por efeito.
- Sidetone/monitoramento: seleção de saída, volume, sinal original/processado, baixa latência medida; desligado por padrão. Distinguir monitoramento físico e digital; prevenir feedback.
- Formatos de entrada suportados (16–192 kHz, 16/24/32 bits, mono/estéreo/multicanal quando disponível), processamento vocal inicial a 48 kHz se compatível. Gravação de teste original/processada, reprodução, pausa, WAV, forma de onda, espectro, ruído, clipping, latência e assistente de calibração.

## 5. Mesa digital e roteamento
- Sete canais iniciais configuráveis: MIC, GAME, CHAT, MUSIC, MEDIA, SYSTEM, AUX/SOUNDBOARD + MASTER. Adicionar/remover canais futuramente, associar fonte real, dispositivo, app, outro mic, instrumento, alertas ou arquivos. Agrupamento visual não é roteamento. Canais sem fonte devem indicar isso.
- Channel strip: nome, ícone, fonte/estado, fader vertical, ganho/trim, volume numérico, VU/peak/clip, pan/balanço, mute/solo, EQ/dinâmica opcional, monitoramento, destinos, lock, reset; polaridade, delay, mono/estéreo, pre/post fader e insert quando aplicável.
- Master: fader, mute, balanço, medição, limiter, bypass, reset, destino; distinto do volume do Windows. Game/Chat Balance com jogo e comunicação independentes, centro neutro, extremos atenuando o grupo oposto, atalhos/reset/presets; nenhum boost não intencional.
- Sessões de apps reais, associações persistentes a canais, reagrupamento, volume/mute e atividade. Instrução de roteamento manual pelas configurações do Windows quando transferência automática não for suportada.
- Modo Normal: mix pessoal e controles comuns. Modo Streamer: mixes diferentes para usuário, transmissão, chat e gravação. Barramentos Personal Mix, Stream Mix, Chat Mix, Recording Mix; volume, mute, limiter, medidores e destino. Permitir ganho por envio individual.
- Matriz fonte x destino: MIC, GAME, CHAT, MUSIC, MEDIA, SYSTEM, AUX para headset, stream, chat, gravação. Mic no chat/stream, monitoramento pessoal opcional; áudio recebido de uma chamada não retorna à mesma chamada (mix-minus); música/jogo não enviados ao chat por padrão. Soundboard só nos destinos escolhidos. Prevenir loops, feedback e reprodução duplicada. Padrão de privacidade: destinos externos desativados até configuração explícita.
- Ducking: fonte gatilho, alvo, threshold, redução dB, attack, hold, release. Solo/PFL/AFL no monitoramento sem silenciar transmissão por acidente. Grupos de faders preservam proporções. Entradas físicas auxiliares existentes, arquivos, fontes virtuais. Medidores RMS/dBFS/peak hold, espectro, waveform; LUFS em etapa avançada. MIDI/HID documentado/Stream Deck/controladores externos como expansão.

## 6. Soundboard / Sample Pads
- Bancos de 4, 8, 16 pads e personalizados: nome, ícone, cor, arquivo, volume, atalho, play/pause/stop, destinos. WAV/MP3 iniciais; avaliar OGG/FLAC/AAC/M4A; arrastar/soltar.
- Gravação rápida (proposta inicial de 15 segundos) e duração avançada de microfone, canal, captura compatível, entrada auxiliar ou mix. Indicador de gravação. Cortar início/fim, volume, fades, normalizar, pitch/velocidade, duplicar/excluir.
- One-shot, loop, hold, toggle, simultâneo, exclusividade de grupo, Stop All. Efeitos próprios/licenciados (palmas, risadas, bipe, alertas, campainha, transição etc.). Roteamento apenas para destinos escolhidos (headset, transmissão, mic virtual, gravação). Censura com bipe, push-to-talk, push-to-mute, mute temporário e atalho de voz aplicados ao fluxo do programa.

## 7. Gravação
Gravar original/processado, saída, stream mix e faixas individuais sincronizadas (mic, game, chat, música, soundboard e mix final); pausa/retomada, reprodução, histórico, exportação WAV primeiro, outros formatos futuramente. Indicador visível, somente ação explícita do usuário, sem gravações ocultas.

## 8. Arquitetura global e DSP
- Stack inicial: C#/.NET 10 LTS, WinForms, NAudio/Core Audio/WASAPI, JSON; xUnit; componentes nativos quando justificados. Compare APOs, endpoints virtuais, captura por processo/loopback, WASAPI shared/exclusive, integração documentada com soluções existentes. Não confunda captura com substituição de áudio. O app simples funciona sem driver; efeitos externos devem ser validados cedo, antes da maior parte dos efeitos.
- Reprodução: app -> endpoint virtual ou APO -> canal/DSP -> barramento -> dispositivo físico. Captura: mic físico -> DSP -> barramento -> endpoint virtual de captura ou APO -> Discord/OBS/jogos. Para mixagem independente, planeje endpoints de render Game/Chat/Music/Media/System/AUX e endpoints de captura Virtual Microphone/Stream Mix/Recording Mix conforme necessidade; minimize quantidade e descreva direções. Pré-requisito para app usar microfone processado: selecionar endpoint apropriado. Exclusivo/ASIO ou efeitos do fabricante podem contornar ou conflitar; informar.
- Avaliar SysVAD/WDK, licença, assinatura, Secure Boot, integridade de memória, instalação/desinstalação/recuperação. Sem driver próprio na primeira etapa; não exigir desativação de segurança. Sem injeção de DLL. APIs privadas somente após explicitar riscos e avaliar alternativas suportadas.
- Motor modular, efeitos independentes com init/process/reset/bypass/parâmetros/latência/dispose. PCM float32, 48 kHz inicial conforme requisitos; resampling, clock drift, alinhamento e compensação de latência, filas/buffers, underruns/overruns, gerenciamento de memória e threads, smoothing anti-click, NaN/Infinity, tratamento de erros, descarte e recuperação. Sem disco/rede/locks demorados no callback. Medir desempenho; não prometer latência zero.
- Cadeia vocal sugerida: captura/resample -> HP/AEC -> noise suppression/gate -> ganho/EQ -> compressor/AGC/de-esser -> pitch/correção -> saturação/ambiente -> limiter -> roteamento. Reprodução: entrada/resample -> preamp/EQ -> dinâmica -> surround/ambiente -> balanço/mix -> limiter -> saída. Ajustar ordem conforme algoritmos e testes; evitar dupla equalização/limitação inadvertida.

## 9. UI, perfis, atalhos e operação
- PT-BR, acessível e simples, WinForms clássica aceitável. Abas planejadas: Início, Headset, Microfone, Mesa de Som, Mixer de Apps, Soundboard, Roteamento, Gravação, Perfis, Testes, Configurações, Diagnósticos, Sobre. Primeiro incremento só apresenta o que está funcional. No topo: dispositivos, perfil, estado, volume, bypass/emergência. Mesa com faders horizontais na disposição dos canais, faders verticais individuais e medidores reais. Modo simples inicia com volume, graves/médios/agudos, boost, voz grave/aguda, ruído, surround, presets, game/chat; avançado mostra parâmetros completos. Tooltips, valores, reset, testes, avisos, teclado, escala de tela, tema claro/escuro opcional e internacionalização conforme requisito abaixo. Recurso indisponível explicitamente desabilitado.
- Perfis completos por dispositivo/uso (Padrão, Jogos/FPS, Música, Filmes, Discord, Streaming, Podcast, Voz grave/natural, ambientes silencioso/ruidoso, Estúdio, Personalizado); JSON versionado, migração, edição/duplicação/renomeação/exclusão/importação/exportação/reset/lock/padrão. Incluir efeitos, volumes, formatos, rotas, canais, buses, pads, atalhos e gravação. Troca por app futura sem injeção.
- Bandeja, iniciar com Windows/minimizado mediante opt-in, manter efeitos quando janela fecha se configurado, alternar perfil/mute/bypass, encerramento real, restauração. Atalhos globais configuráveis, sem conflitos. Botão de emergência e modo seguro.

### Internacionalização e manutenção (decisão aprovada em 18/11/2026)
- Config → Idioma: português brasileiro padrão inicial, escolha manual ou seguir cultura de interface do Windows. Seleção persistida em configuração versionada, migração compatível, fallback pt-BR para idioma/recurso indisponível. Sem rede, tradução automática em execução ou alteração da linguagem do Windows. Aplicar sem reiniciar quando seguro; caso necessário, informar reinício normal sem iniciar captura/monitoramento nem perder ajustes.
- Recursos de tradução locais e centralizados (por exemplo .resx/ResourceManager e culturas BCP 47); todas as telas, navegação, diálogos, tooltips, acessibilidade, mensagens de erro/aviso, estados, bandeja, ajuda e textos próprios do aplicativo. Nomes reais de dispositivos/apps, dados do usuário, unidades técnicas, códigos de erro, chaves JSON e identificadores continuam estáveis. Separar formatação cultural de números/datas da persistência invariável.
- Expandir para o máximo viável de idiomas com traduções revisadas e completas. Prioridade proposta, não lista já entregue: pt-BR, en, es, pt-PT, fr, de, it; depois zh-Hans, zh-Hant, ja, ko, ru, ar, hi, tr, pl, uk e outras culturas conforme qualidade. Testar expansão de texto, Unicode/fontes, DPI, teclado, pluralização, RTL (árabe e outros) e fallback. Somente exibir idiomas realmente suportados; nenhuma opção fictícia. Implementar infraestrutura na primeira etapa de UI após F3, incrementar traduções por módulo, finalizar cobertura antes da distribuição.
- Comentários explicativos de todo código próprio/testes/scripts em pt-BR, preservando licenças/terceiros/gerados, diretivas, identificadores e semântica. Tradução do legado em commit separado, auditado por tokens/sintaxe, sem alterar comportamento. Não bloquear a prova física atual para misturar alterações textuais ao motor.

## 10. Privacidade, estabilidade, desempenho e distribuição
Processamento local, sem login/publicidade/envio de áudio/telemetria desnecessária, sem gravação silenciosa, consentimento para instalações/elevacão, logs locais sem conteúdo de áudio, backup e restauração de configurações alteradas, proteção de picos, prevenção de feedback e recuperação após falha. Não sobrescrever drivers físicos. Baixo CPU/RAM/latência e uso durante jogos; efeitos desligados não processam; atualização de UI moderada. Diagnóstico: CPU/RAM, latência medida/estimada distinguida, buffer, taxa, canais, dispositivos, rotas, efeitos, underruns/overruns, erros e versão. Instalador/desinstalador e `WaveStudio.exe`, publicação self-contained quando apropriado, ícone/atalhos, versão portátil se possível; avaliar Inno Setup/WiX; separar drivers, verificar licenças e assinatura e limpar/restaurar após desinstalação.

## 11. Entregas e ordem de implementação
- Fase 0: viabilidade, documentação, matriz suporte/limites, arquitetura e licenças; decidir caminho de integração global.
- Fase 1 (v0.1.0 Alpha): janela, endpoints, padrão, seleção, volume/mute, balanço quando suportado, sessões reais e volumes/mute, medidores, hotplug e configurações. Sem driver próprio. Build e testes.
- Fase 2: motor de captura/reprodução, buffers, formato, resampling, monitoramento, gravação de teste e latência.
- Fase 3: prova real de DSP em navegador e microfone processado em OBS/Discord, usando rota/driver/APO viável, sem loop; restauração. Não avançar para efeitos em massa sem documentar o resultado/bloqueio.
- Fase 4: mesa básica, canais/faders/master/game-chat/solo, bus pessoal e medição.
- Fase 5: múltiplos endpoints, barramentos, matriz, mix-minus, ducking e monitoramento independente.
- Fase 6: EQ headset e dinâmica, presets, brilho, graves e clareza.
- Fase 7: tratamento mic, boost, EQ, gate, supressão, compressor, limiter, de-esser, AGC, saturação, AEC.
- Fase 8: soundboard e pads com gravação, edição, atalhos e roteamento.
- Fase 9: alteração vocal, pitch/formantes/correção, ambientes e efeitos.
- Fase 10: surround e HRTF, shifter, áudio espacial e testes de canais.
- Fase 11: gravação multifaixa, perfis e automação avançada.
- Fase 12: otimização, compatibilidade, documentação, instalador, desinstalador e publicação.

Estrutura sugerida (ajuste sem burocracia): `src/WaveStudio.Desktop`, `WaveStudio.Core`, `WaveStudio.Audio`, `WaveStudio.DSP`, `WaveStudio.Mixer`, módulos Soundboard/Recording/Profiles/Native conforme crescimento; `tests/`, `assets/`, `drivers/`, `installer/`, `docs/`, `README.md`, `AGENTS.md`, `PROJECT_STATUS.md`, `CHANGELOG.md`, solução .NET (`.sln` ou `.slnx` conforme ferramenta). Documente arquitetura, testes e requisitos. Não sobrescreva este documento silenciosamente: registre decisões/alterações.

## 12. Critérios verificáveis e testes
- Fase 1: abrir janela e identificar dispositivos reais, selecionar entrada/saída sem efeitos colaterais, modificar volume e conferir Windows, mute app sem afetar outros, balanço real, medidores reagindo, desconectar/reconectar, perfil restaurado após reinício e mensagens de erro. Teste manual em máquina Windows é obrigatório para confirmar interação real com hardware.
- DSP: senos, impulsos, ruído branco/rosa, silêncio, clipping, estéreo/multicanal e taxas diferentes; verificar ganhos/resposta em frequência, pitch/formantes, harmônicos, redução de ruído, teto de limiter, ausência de NaN e estabilidade. Teste latência real e recuperação.
- Mesa: game/chat independentes, master, grupo, mute/solo, personal vs stream, matriz e isolamento, mix-minus, ducking. Soundboard: importação, gravação, pad, Stop All, destinos e ausência de vazamento. Gravação: alinhamento temporal das faixas.
- Integração: navegador, OBS, Discord, jogo, USB, analógico, Bluetooth, hotplug, suspensão e retomada, encerramento inesperado, instalação/desinstalação, restauração. Não simular funcionamento global com loopback isolado.
- Concluído = código real + compilação + integração com motor + teste objetivo + erros tratados + documentação + limitações. Diferencie testes automatizados, integração e testes físicos pendentes. Sem números inventados.

## 13. Fontes técnicas a consultar
- Microsoft Core Audio/WASAPI: https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi
- Windows Driver Samples (SysVAD): https://github.com/microsoft/Windows-driver-samples
- NAudio: https://github.com/naudio/NAudio
- RNNoise: https://github.com/xiph/rnnoise
- WebRTC: https://webrtc.org/
- Equalizer APO: https://sourceforge.net/projects/equalizerapo/
- OBS: https://github.com/obsproject/obs-studio
- Fabricantes: https://www.redragon.com.br/ e https://fifinemicrophone.com/ (validar modelos/manuais e licenças)

A especificação define escopo desejado. A arquitetura e a viabilidade de recursos avançados devem ser confirmadas; nenhuma referência comercial equivale a garantia de suporte universal.

## Adendo aprovado — três modos de equalização

Básico 10 bandas ±15 dB; Avançado 31 bandas, ±30 dB condicionados à segurança/validação; Personalizado com filtros, frequência/ganho/Q editáveis, inclusive exemplo de 13 frequências do proprietário. Aplicável aos EQs independentes de Áudio e Microfone. Detalhes e restrições no [plano mestre](PRODUCT_MASTER_PLAN.md). Preservar requisitos anteriores e execução por fases.
