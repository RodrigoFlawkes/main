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


## 12/11/2026 — Fase 1, Incremento 4

Commit revisado: [b63ef63](https://github.com/Wave-Universal-Studio/main/commit/b63ef63304cb6a463565a2bbbe270d6bd1ce2325).
Parecer: aprovado para prosseguir à F1-I5, com correções de recuperação e persistência segura. A aceitação física não está concluída.

### Evidências
- Revisão estática de AudioWatchEngine, AudioWatchWorker, AudioWatchModels, CoreAudioWatchBackend, DeviceSelection, MainForm, DevicePanel, ApplicationMixerPanel, ApplicationSessions e testes de watch/seleção.
- Worker MTA persistente, fontes reutilizadas, callback sinalizador, coalescência, fallback, descarte ordenado e snapshots por geração. Grid reconciliada por chave e protegida por binding/geração; busca de sessão tolera falha de item não relacionado. Seleções explícitas e automáticas separadas.
- PROJECT_STATUS.md registra 101 testes aprovados (95 regras/simulações/grid STA, 6 integrações Windows de leitura) e build sem erros/avisos. Integrações observaram zero nos picos de entrada/saída e cinco sessões em leitura pontual. A média de 0,083 ms é de consulta em uma amostra curta, não latência de áudio nem garantia de CPU. O revisor não executou testes independentemente.
- Medidores contínuos por sessão continuam pendentes; não confundir a leitura pontual com uma funcionalidade entregue. Fala/música, USB, alteração de padrão, suspensão/retomada, escritas, DPI, estabilidade longa e Windows 11 continuam pendentes.

### Correções para F1-I5
1. AudioWatchWorker termina se escapar exceção no loop, mas Latest pode continuar contendo o último pico e status "ativa". Publicar estado de falha/indisponibilidade ou permitir recuperação segura, sem exibir snapshot antigo indefinidamente. Considerar timestamp/freshness e invalidação no retorno da suspensão. Testar falha terminal simulada e fechamento, sem reinício em loop ocupado.
2. OnPowerModeChanged chama Recover, mas a atualização recebida usa RequestRefresh(false). Com o mesmo ID/estado/formato, DevicePanel e mixer podem conservar falhas latched de comandos após retomada ou reconexão rápida. Definir evento de recuperação distinto de refresh de metadados, invalidando gerações e erros quando realmente necessário, mesmo se ID for igual. Evitar reset de controles a cada notificação irrelevante. Manual refresh deve permitir tentar recuperar inscrição/fontes sem criar loop de catálogo. Testar com ID igual, sem escritas.
3. Ao carregar preferências persistidas, preservar modo automático ou ID explícito ausente independentemente de existir no catálogo atual. Adicionar API de seleção validada por direção/ID sem fabricar DeviceSnapshot. Corrigir modo automático se houver padrão marcado mas inativo; avaliar fallback ativo e informar quando o padrão real não está disponível. Não converter fallback em preferência explícita. Não escrever volume durante inicialização, refresh, troca de padrão, hotplug ou recuperação.
4. ApplicationMixerPanel.ReadAsync pode perder a tentativa imediata de atualização pós-comando se uma leitura anterior estiver ativa; polling posterior recupera, mas verificar reconciliação pronta e que erros/edição não sejam perdidos. Ajuste pequeno se necessário, sem reescrever mixer.
5. Reavaliar viabilidade de medidor contínuo por sessão com orçamento e ciclo de vida, sem atrasar F1-I5; registrar como pendência da F1-I6 caso não implementado. Não adicionar controles fictícios. Manter testes físicos e visualização pendentes de forma explícita.

Itens 1–3 devem ser resolvidos ou ter bloqueio específico registrado antes de concluir F1-I5. F1-I6 será a etapa de validação, não uma aprovação automática. A partir dela é necessário obter evidências reais de escrita e áudio ativo; o proprietário pode realizar testes guiados se Computer Use continuar indisponível. O design profissional permanece planejado após a base Alpha; não antecipar DSP ou instalar drivers.


## 12/11/2026 — Fase 1, Incremento 5

Commit revisado: [15797ee](https://github.com/Wave-Universal-Studio/main/commit/15797eeb2b958f6a4be2d3ca885c3ed960fd5af2).
Parecer: aprovado para F1-I6 (validação e empacotamento), não para declarar a Alpha fisicamente aceita.

### Evidências
- Revisão estática de SettingsStore, StudioSettings, WindowPlacement, MainForm.Preferences, MainForm, EndpointPreferenceRestore, EndpointVolumeSession, EndpointControls, AudioWatchWorker, Program e testes de armazenamento/restauração.
- Configuração v1 separa seleção persistida de estado atual do Windows; arquivo futuro protegido, cópia temporária e backup, detecção de conflito, restauração explícita com confirmação e geração. Retomada e falha de worker tratadas. PROJECT_STATUS.md relata 174 testes (165 regras/simulações/arquivos/STA e 9 integrações Windows) e build sem erros/avisos. Não foram repetidos pelo revisor.
- Execução em armazenamento isolado e reinício observados pelo Codex. Nenhum teste físico de escrita ou áudio ativo comprovado. Preferências duráveis por sessão e medidores contínuos de sessão pendentes.

### Pontos para F1-I6
1. SettingsStore.ReadOptional rejeita arquivos >1 MiB antes de obter baseline. Load marca Corrupt mas Reset/Recover reexecutam leitura que pode lançar InvalidDataException (ou baseline anterior não correspondente). Não oferecer operação que não pode concluir: classificar arquivo excessivo como protegido, bloquear escrita/reset/recuperação automática e explicar preservação/inspeção manual; tratar mudança de arquivo durante operação sem exceção não observada. Testar arquivo grande, crescimento concorrente e byte a byte preservado. Não ler arquivo arbitrariamente grande para arquivar.
2. DrainSettingsSavesAsync captura apenas cancelamento; SettingsJson.Encode pode lançar por validação (por exemplo limite de 1000 endpoints ou janela), e exceção pode ficar em settingsTask e escapar do fechamento. Tratar falhas de validação/I/O previsíveis com mensagem; impedir encerramento travado e confirmar que não se altera arquivo válido. Validar que nenhum valor indisponível é salvo como preferência utilizável. Desabilitar controles de seleção até carga inicial; testar ações antes da carga, mudança durante confirmação e fechamento.
3. EndpointPreferenceRestore confirma volume/mute no estado final, mas não confere explicitamente balanço final após os comandos restantes. Revalidar canais/posição final com tolerância em escala de canal adequada à quantização, tratar base zero como balanço não observável e não prometer confirmação impossível. Se houver divergência real, retornar incompleto; avisar que desmutar também pode tornar áudio audível mesmo sem aumento de volume. Não aumentar níveis durante abertura, seleção ou recuperação. Testar driver que modifica canais após comando e ausência de suporte. 
4. Testar a aplicação em uso real com roteiro assistido; Computer Use tem falhado em captura e cliques. Não substituir testes físicos por simulação. Obter evidência do proprietário ou marcar bloqueado. Testar com volume baixo e registrar/restaurar níveis; não suspender, desconectar ou modificar configurações críticas sem ação/autorização.
5. Medidores contínuos de sessão: avaliar orçamento e ciclo de vida; implementar se viável com escopo controlado, senão registrar pendência e condição de entrega. Preferência durável de sessão continua condicionada a identidade inequívoca, não insistir em PID/nome. Não antecipar o motor de áudio.
6. Revisar publicação x64 Release, conteúdo do pacote, segurança/privacidade, licenças de dependências e instrução de execução. Não declarar compatibilidade Windows 11 sem ensaio; Windows 10 experimental. A Alpha pode ser distribuída para teste local com rótulo de prévia, mas aceitação plena depende das evidências físicas. Não criar instalador/driver agora.

As referências adicionais do proprietário (Peace/Equalizer APO, Light Host e efeitos VST) foram registradas em docs/FUTURE_AUDIO_FEATURES.md para planejamento posterior. Não expandir F1-I6 para implementá-las. 

## 12/11/2026 — checkpoint F1-I6 e teste de microfone

Commit de referência: 4c05092. O roteiro de aceitação informa 184 testes em Debug/Release e executável local; a aceitação física segue pendente. O proprietário confirmou volume e mute do microfone no novo teste. A captura mostra microfone mono de um canal; manter balanço L/R desabilitado e indicar o motivo de modo claro. Não simular estéreo nem alterar driver. Foi solicitada uma forma de ouvir a própria voz; adendo limitado de teste de captura/reprodução, sem antecipar o motor inteiro. Roteiro atualizado com evidências parciais; não declarar A01–A10 aprovados nem finalizar a Alpha.

## 13/11/2026 — medidor na aba Testar microfone

Base fcb147a. Inspeção direcionada de MainForm.cs e MicrophoneTestPanel.cs: o worker de picos existente atualiza DevicePanel em timer de 100 ms, mas habilita medição somente quando a aba Dispositivos está selecionada. A aba Testar microfone não mostra pico. O proprietário solicitou barra de captação semelhante à do Windows. Reutilizar leitura do endpoint selecionado, mantendo seleção/geração/frescor e visibilidade; sem abrir gravação ou monitoramento automaticamente. Não apresentar leitura indisponível como silêncio. Se o driver só expõe pico durante captura ativa, explicar limitação e conferir durante gravação explícita. A11: proprietário relatou zumbido sem voz no Wave Studio e também em teste Windows; posteriormente indicou melhora no Windows, mas troca USB-C/C por C/A não resolveu o problema relatado. Estado inconclusivo, não atribuir causa ao cabo, hardware ou aplicativo sem evidência comparativa. Não declarar aceitação física.
