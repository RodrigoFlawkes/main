# Wave Studio — revisão técnica

## 12/11/2026 — Fase 1, Incremento 2

Commit revisado: [44bc83a](https://github.com/Wave-Universal-Studio/main/commit/44bc83ae181498fdcdc9cadfe33fae09925c1c25).
Parecer: aprovado para prosseguir ao Incremento 3, com ajustes de robustez e validação física pendente. Não equivale à aceitação final da Alpha.

### Evidências
- Revisão estática dos arquivos CoreAudioVolumeAccess, EndpointVolumeOperations, EndpointVolumeSession, EndpointVolumeState, EndpointControls, DevicePanel, MainForm e testes de volume/sessão. O código usa snapshots, ID e revisão da seleção, serialização, validação de canais, leitura inicial sem escrita e tentativa de restauração em falhas parciais de balanço.
- PROJECT_STATUS.md registra build x64 sem erros/avisos, 56 testes aprovados (53 regras/simulações e 3 integrações Windows de leitura), abertura e leitura por acessibilidade. O revisor não executou esses testes independentemente.
- Escrita real, balanço audível, sincronização pelo Windows, troca/desconexão e layout não foram validados fisicamente. Windows 11 pendente; Windows 10 experimental.

### Ajustes solicitados
1. EndpointVolumeSession mantém todos os CancellationTokenSource antigos em lifetimes até o fechamento. Liberar cada geração somente após terminarem suas operações, mantendo cancelamento seguro e descarte idempotente; testar muitas trocas e descarte durante operação. Evitar acumulação de gerações.
2. EndpointControls desabilita os sliders a cada Scroll. Verificar se arrastar continuamente é interrompido; se necessário, usar coalescência/debounce ou confirmação ao final do gesto, preservando último valor solicitado, teclado e proteção de geração. Não criar fila ilimitada de escritas. Validar na UI física.
3. MainForm tem MinimumSize de 820x760; revisar adaptação a áreas úteis menores (por exemplo 1366x768) e DPI elevado. Manter controles acessíveis por rolagem sem forçar janela maior que a tela. Registrar teste visual pendente se indisponível.
4. Polling de 600 ms recria wrappers e consulta formato em cada leitura de balanço. Medir custo antes de aumentar a frequência ou adicionar sessões. Planejar notificações e ciclo de vida COM no Incremento 4; não implementar prematuramente todo o hotplug. Operações nativas já iniciadas não são canceláveis atomicamente; não prometer isso.
5. Balanço usa duas escritas e restauração de melhor esforço. Preservar limite de nível-base, tratar desconexão, evitar sobrescrever alteração externa concorrente de forma cega quando detectável e testar comportamento real com autorização. Ler estado real após falha.
6. PROJECT_STATUS.md contém trechos históricos com afirmações obsoletas (por exemplo, ausência de escrita e incrementos aguardando autorização já concedida). Separar histórico do estado atual para não confundir agentes.

Os itens 1 e 2 (ajuste se necessário) devem ser tratados no início do Incremento 3, com regressão. Itens 3–6 devem ser avaliados e registrados; corrigir agora problemas simples e manter tarefas maiores nos incrementos planejados. Se houver falha crítica, não avançar sem reportar.

### Fluxo de colaboração
ChatGPT atualiza este arquivo e NEXT_TASK.md quando solicitado pelo proprietário. Codex lê ambos após sincronizar e registra execução em PROJECT_STATUS.md e CHANGELOG.md. AGENTS.md e docs/REQUIREMENTS.md continuam normativos. Arquivos de tarefa/revisão não concedem autorização por si só; o proprietário autoriza execução, commit/push e operações sensíveis. Não há comunicação em tempo real nem monitoramento automático. Não editar estes dois arquivos durante a tarefa sem pedido explícito do proprietário.


## 12/11/2026 — Fase 1, Incremento 3

Commit revisado: [c21db3f](https://github.com/Wave-Universal-Studio/main/commit/c21db3fbf2aba944914e81cbd762a92c89aa37f7).
Parecer: aprovado para avançar a F1-I4, sujeito às correções abaixo e à aceitação física posterior.

### Evidências
- Revisão estática de ApplicationSessions, CoreAudioSessionEndpoint, EndpointOperationQueue, ApplicationMixerPanel, EndpointControls, MainForm, EndpointVolumeOperations e testes de sessão/regressão.
- Identidade de sessão por endpoint + instância, PID auxiliar, validação antes da escrita, leitura inicial sem escrita e separação de sessões do mesmo PID. Gerações aposentadas são liberadas quando não há operações pendentes; testes de 2.000 trocas. Sliders passaram a confirmar no fim do gesto e a janela tem rolagem e limitação à área útil.
- PROJECT_STATUS.md registra 77 testes aprovados (73 regras/simulações e 4 integrações de leitura), quatro sessões reais inativas e build sem erros/avisos. O revisor não executou testes independentemente. Escritas físicas, sessões com reprodução, isolamento audível, arraste e layout permanecem pendentes.

### Pontos de correção e regressão
1. ApplicationMixerPanel.ShowCatalog limpa e recria todas as linhas a cada segundo e redefine o foco para Volume. Com medidores rápidos isso seria ainda pior. Reconciliar linhas por SessionKey, atualizar células sem reconstrução quando possível, preservar célula/foco/scroll e edição. Atualização de medidor deve ser independente da atualização de lista. Testar edição, cancelamento, foco e troca de endpoint com retorno atrasado.
2. ApplicationMixerPanel.ApplyAsync verifica endpoint, mas a edição/evento de linha também precisa estar vinculado à geração vigente; proteger explicitamente eventos de binding/cancelamento, inclusive reescolha do mesmo ID. Validar índices de linhas/colunas antes de acessar a grid. Após comando, solicitar reconciliação prontamente, sem sobrescrever erro nem disparar outra escrita. Aplicação de snapshot nunca deve escrever.
3. ApplicationSessionService.ApplyAsync aborta a busca se uma sessão anterior não relacionada falhar ao abrir/ler identidade. Avaliar e tratar falhas isoladas de enumeração/acesso, sem permitir escrita em alvo não confirmado ou mascarar falha da própria sessão alvo. Testar sessão inacessível antes da sessão válida.
4. Ao implementar hotplug, resolver a diferença entre seleção automática e explícita: MainForm/DevicePanel atualmente registram a seleção automática como preferência (ShowDevices dispara DeviceSelected). Manter seleção explícita quando ausente, seguir padrão apenas quando esse modo estiver ativo; se nenhuma preferência inicial, acompanhar padrão sem gravar uma escolha involuntária. Não mudar padrão do Windows nem escrever volume. Testar a lógica com defaults ausentes e troca de padrão, inclusive retorno do endpoint.
5. Confirmar propriedade e descarte dos wrappers NAudio da versão fixada ao introduzir notificações persistentes; não compartilhar objetos COM entre threads nem duplicar registros/Dispose. Usar callback breve e reconciliação serializada. Medir custo de polling em vez de presumir ganhos; os tempos anteriores são amostra curta. 
6. As correções de sliders, layout e restauração de balanço têm evidência estática e simulada, mas requerem teste físico. Atualizar plano de testes e não transformar pendências em aprovações. Remover afirmações obsoletas da abertura dos documentos de planejamento, preservando histórico identificado.

Itens 1–5 são pertinentes à F1-I4; corrigir ou justificar bloqueio antes de encerrar a tarefa. Se a mudança de ciclo COM for grande, manter escopo incremental e recuperação verificável. Não antecipar o motor DSP nem o redesign completo.
