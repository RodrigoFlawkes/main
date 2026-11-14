# Wave Studio — hub, organização funcional e novos requisitos propostos

Registro de 14/11/2026 do rascunho do proprietário. Complementa REQUIREMENTS.md e FUTURE_AUDIO_FEATURES.md, não substitui requisitos normativos nem declara implementação. Não muda a tarefa EQ-PREVIEW-1 em andamento. Produto funcional primeiro, design profissional depois. Esforço Alto por padrão e economia de cota; nenhuma instalação/elevação/operação crítica autorizada por este documento.

## Navegação futura
Barra lateral compacta, ícones com nomes curtos abaixo. Hub abre visão geral, atalhos, dispositivos selecionados/padrão, estado de processamento e alertas. Proposta de áreas (rótulos ajustáveis no redesign):
- Hub
- Áudio (equalizador de reprodução)
- Microfone (equalizador de captura)
- Mixer (mesa de som digital)
- Voz (efeitos divertidos e Soundboard)
- Tratamento (DSP avançado e surround)
- Reparos (solução de problemas)
- Apoiar (doação voluntária)
- Ajustes (preferências gerais)

Não retirar abas/recursos existentes nem mudar a interface atual nesta etapa. Quando houver mais funções, agrupar páginas e manter acesso a rotas, perfis, gravação, diagnósticos e configurações. Ícones acessíveis, rótulos, teclado e estados indisponíveis explícitos. O desenho visual completo fica para etapa posterior à consolidação funcional.

## Áudio e Microfone
Seletores independentes de entrada/saída, indicar padrão Windows multimídia/comunicações e permitir definir padrão somente por mecanismo público suportado ou abrir configuração nativa para escolha explícita. Selecionar dispositivo no Wave Studio não significa alterar padrão Windows. Testes de áudio (tom/estéreo/canais conforme suporte, volume digital inicial baixo) e de microfone (captura curta original/processada, A/B, medidor; monitoramento contínuo após validação). EQ gráfico 10 bandas e paramétrico, preamp, presets, bypass, reset, curva de resposta real, proteção; separar ganho Windows/digital/físico. Configurações independentes de entrada e saída. Não iniciar som/captura automaticamente.

## Mixer e mesa digital
Mesa virtual que emula organização de console físico, com faders, canais MIC/GAME/CHAT/MUSIC/MEDIA/SYSTEM/AUX/SOUNDBOARD e MASTER, medição, ganho, pan, mute/solo, EQ/dinâmica por canal, buses, matriz e mix-minus conforme requisitos. Simulações para desenvolvimento/testes ou protótipos devem ser rotuladas; não disponibilizar controles fictícios como funcionais. Roteamento externo depende de prova de integração.

## Voz e Soundboard
Presets iniciais pedidos: Eco, voz fina, grossa, feminina e masculina estilizadas. Sugestões para os dois adicionais: robô e monstro. Pitch/formantes quando aplicável, sem afirmar transformação natural perfeita; preservar voz grave natural como opção distinta dos efeitos cômicos. Soundboard com pads, arquivos próprios/licenciados, gravação explícita, volume, atalhos, stop e destinos conforme recursos reais. Demais presets e efeitos anteriores continuam previstos.

## Tratamento
Compressor, supressão de ruído, isolamento de fala (se tecnicamente viável), saturação, reverb, redução de reverberação/de-reverb, noise gate, limiter, de-esser, AGC, AEC conforme viabilidade e requisitos anteriores. Diferenciar isolamento de fala de isolamento acústico físico, supressão de gate, reverb de de-reverb. Cadeia modular e ativação individual, medição antes/depois e presets. Surround 7.1: detectar capacidades, virtualização HRTF para estéreo onde viável, shifter e teste de canais; não prometer universalidade nem chamar expansão estéreo de 7.1 real. Sem efeitos globais até rota comprovada.

## Reparos — novo módulo proposto
Inspirado funcionalmente na solução de problemas de áudio de Driver Booster; não copiar marca/interface nem criar atualizador genérico de drivers. Escopo progressivo:
1. Diagnóstico somente leitura: endpoints ativos/ausentes, estado, mute, níveis, padrão, formato, serviços de áudio, identificação/versão/estado do driver quando expostos, erros e teste guiado, com resultado verificável e ação sugerida. Distinguir diagnóstico, hipótese e reparo.
2. Assistente de correções reversíveis e específicas (seleção de padrão, nível/mute, formato via configurações suportadas, abrir páginas nativas de áudio e instruções). Mostrar antes/depois, pedir confirmação para alteração; preservar preferências e evitar interromper chamadas/streams.
3. Avaliar reinício de serviço de áudio e atualização/reinstalação/rollback de driver somente quando justificável, por ferramentas oficiais Windows/fabricante, fontes verificadas e assinadas, backup/recuperação e autorização específica. Nunca desinstalar ou substituir drivers automaticamente, instalar drivers de origem desconhecida, elevar sem consentimento, alterar segurança, baixar/executar binários não verificados ou prometer reparo universal. Se não houver suporte seguro, orientar intervenção manual. Não tornar manutenção de drivers pré-requisito do EQ/MVP.
Critérios: ações em ambiente simulado primeiro, identificação correta, falhas tratadas, confirmação explícita, documentação e verificação pós-reparo; ensaios físicos apenas com autorização. Logs locais sem segredos/áudio.

## Apoiar — novo módulo proposto
Página discreta "Apoie o desenvolvedor", doação voluntária sugerida US$ 1 (ou valor livre, se plataforma escolhida permitir), sem bloquear recursos ou inserir anúncios. Link oficial de pagamento externo definido pelo proprietário mais tarde; nenhuma conta, credencial, endereço de pagamento ou plataforma inventada. Não coletar dados de cartão no aplicativo, não simular pagamento, não exibir botão de pagamento ativo sem destino validado. Avaliar taxas/moeda e termos quando escolher plataforma. Não implementar agora.

## Ordem
Manter EQ-PREVIEW-1; retomar infraestrutura indispensável F2-I6 e prova externa F3 antes de expansão em massa. Reaproveitar DSP modular. Mixer e EQs/efeitos/roteamento conforme requisitos e dependências; diagnósticos básicos em momento oportuno, correções avançadas após revisão; doação e redesign posterior. Nenhuma ampliação automática do escopo atual nem consumo de cota com mockups/código vazio.
