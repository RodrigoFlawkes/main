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
