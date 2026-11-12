# Wave Studio — próxima tarefa

ID: F1-I6-MIC
Base: 4c05092 (checkpoint técnico F1-I6).
Estado: adendo de teste de microfone solicitado pelo proprietário; aceitação física ainda pendente.
Esforço recomendado: Alto. Motor de monitoramento contínuo da Fase 2 será tratado separadamente com Extra alto.

## Fluxo
- Confirmar remoto/branch main e git status. Se limpo, git pull --ff-only; se divergente, preservar e reportar. Ler AGENTS.md, REQUIREMENTS, arquitetura, plano, PROJECT_STATUS.md, REVIEW.md, ALPHA_ACCEPTANCE.md e FUTURE_AUDIO_FEATURES.md. Não repetir F1-I6 inteiro. Não alterar NEXT_TASK.md ou REVIEW.md. Commit/push apenas se autorizado. Não instalar drivers, elevar privilégios, alterar padrões ou configurações críticas.
- O proprietário confirmou volume e mute do microfone funcionando. Endpoint captura mono (1 canal): manter balanço L/R indisponível, com explicação próxima do controle. Não corrigir como defeito nem fabricar canais.

## Objetivo
Adicionar opção Testar microfone na interface atual, para ouvir a própria voz, com gravação curta seguida de reprodução local explícita. É ferramenta de diagnóstico limitada, não motor de DSP ou sidetone contínuo. Reutilizar o componente na Fase 2 quando adequado.

### Implementação
1. Botões Gravar teste, Parar, Ouvir gravação, Parar reprodução e Limpar teste, conforme estados disponíveis. Duração máxima sugerida de 10 s; indicador visível de captura e tempo. Captura só após ação explícita, nunca ao abrir/selecionar dispositivo. Usar endpoint de captura selecionado e reprodução no endpoint de saída selecionado explicitamente, sem fallback silencioso. Não alterar nível/mute/padrões Windows. Usar APIs públicas NAudio/WASAPI da versão fixada, conferir formatos suportados e ownership. Se conversão for necessária, suportar subconjunto seguro negociado ou reportar incompatibilidade; não prometer suporte universal.
2. Buffer em memória com limites de duração e bytes, sem gravação em arquivo, rede, telemetria ou persistência de áudio. Limpar ao trocar dispositivos, fechar ou solicitar descarte. Nunca reproduzir automaticamente após gravar. Bloquear simultaneidade entre gravação e reprodução de teste, prevenir loops e iniciar playback em nível digital conservador, com controle de volume do teste independente, sem boost. Aviso para usar fones e volume baixo. Botão parar disponível; stop e descarte também em desconexão, mudança de alvo, suspensão e encerramento. Não usar microfone real durante testes automatizados sem consentimento específico.
3. Separar Audio de WinForms, estados testáveis, callbacks curtos, buffers limitados, cancelamento e geração; sem chamadas UI/COM impróprias. Mostrar falhas, formatos incompatíveis e mute sem alterar controle do sistema. Testar regressão dos 184 casos, estados, limite, cancelamento, seleção obsoleta, desconexão, encerramento, nenhuma captura/reprodução automática, nenhuma escrita de configuração de áudio. Simulações e integração de leitura separadas de teste físico.
4. Atualizar documentação, ALPHA_ACCEPTANCE.md (novo caso A11: gravar/falar/parar/ouvir em headset, descartar; pendente até evidência física), PROJECT_STATUS e CHANGELOG. Restore locked, build Debug/Release, testes, diff check e republicação local Release x64 em artifacts/alpha-preview/win-x64. Se exe aberto, orientar fechar normalmente; não matar processo do usuário. Manter licenças, não versionar artefatos/dados pessoais. Se autorizado, commit feat: add local microphone recording and playback test, push normal e verificar sincronização.
5. Monitoramento ao vivo fica no primeiro planejamento da Fase 2, não implementar agora para evitar feedback e reescrita do pipeline. Não iniciar Fase 2, design completo ou declarar Alpha aceita. Relatar limitações e instruções de teste do novo exe. Pendências A01–A10 continuam válidas.
