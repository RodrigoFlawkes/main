# Changelog

## Em desenvolvimento

### 2026-11-12 — F1-I6-MIC, teste local de microfone

- Aba Testar microfone com gravação explícita de até 10 s, parada, reprodução explícita na saída selecionada, parada da reprodução e descarte. Sem monitoramento contínuo.
- Trecho em memória limitado a 8 MiB e 10 s, sem arquivo/envio/persistência. Descarte na mudança de alvos/formato, atualização, desconexão, suspensão e fechamento.
- Nível digital de reprodução inicialmente em 20%, com ajuste 0–100% e sem boost. Preservados volumes/mute/padrões do Windows e balanço indisponível em mono.
- Worker MTA separado da UI, snapshots, cancelamento/geração, callbacks limitados e descarte de streams/dispositivos. APIs públicas WasapiRecorderBuilder/WasapiPlayerBuilder da versão 3.1.0, sem novas dependências.
- Restore locked com auditoria, builds Debug/Release sem erros/avisos e 228 testes aprovados em cada configuração (184 anteriores + 44 novos). Novo executável conferido em abertura/fechamento com configuração isolada, sem gravação real.
- Roteiro A11 e documentação atualizados. Volume/mute de captura confirmados pelo proprietário são evidências parciais; A01–A11 e Windows 11 continuam aguardando aceitação física completa.
- Após confirmação de fechamento normal da prévia anterior, pacote F1-I6-MIC promovido para artifacts/alpha-preview/win-x64 com conferência dos 14 arquivos por SHA-256. Caminhos de execução atualizados; código e dependências preservados.

### 2026-11-12 — F1-I6, checkpoint técnico de validação e pacote x64

- Sincronizado até 8d0e688 e tratados os pontos 1–3 da revisão F1-I5. Arquivo excessivo recebe estado protegido, com escrita/reset/recuperação bloqueados e preservação em crescimento concorrente.
- Falhas previsíveis de validação/gravação são informadas e observadas no fechamento. Seleções ficam desabilitadas até a carga. Valores indisponíveis e balanço em silêncio são tratados ao salvar.
- Restauração confirma canais finais após unmute, com tolerância de 2% do nível-base, informa balanço não observável em zero e avisa sobre áudio audível ao confirmar unmute.
- Pico real por sessão no mixer a cada segundo, com identidade por instância, descarte dos wrappers, pausa durante edição e indicação de atraso/indisponibilidade.
- Restore locked, builds Debug/Release com zero erros/avisos e 184 testes aprovados em cada configuração, incluindo os 174 anteriores.
- Publicação local framework-dependent AMD64, avisos de licença, SHA-256 e roteiro ALPHA_ACCEPTANCE. Processo ensaiado por 600,34 s e encerrado normalmente, com dados de CPU/RAM/handles registrados.
- Automação visual indisponível por falha do pipe nativo. Validação física e Windows 11 aguardam evidências do proprietário. A Alpha permanece com validação pendente.
- Após o checkpoint 06d2c3f, proprietário confirmou que testará a prévia pronta e que dispõe somente de Windows 10. Roteiro e status atualizados para iniciar A01/A02, com Windows 11 pendente por ambiente indisponível.

### 2026-11-12 — F1-I5, preferências versionadas e recuperação segura

- Sincronização até 4e2d690 e execução da tarefa autorizada, preservando NEXT_TASK.md, REVIEW.md e requisitos.
- Configuração JSON por usuário com schemaVersion 1, seleções por direção, modo automático/ID explícito ausente, janela validada e preferências opcionais de volume/mute/balanço por endpoint.
- Salvar valores atuais, prévia e confirmação para restaurar com alvo/geração/suporte/níveis revalidados, consulta após aplicação e indicação de falha parcial.
- Escrita fora da UI com exclusão, hash de conflito, temporário na mesma pasta, flush, substituição e backup. Corrupção e versão incompatível preservadas, recuperação/reset explícitos e originais inválidos arquivados.
- Falha terminal e leituras antigas do worker agora exibem indisponibilidade. Retomada/manual e eventos de ciclo de vida invalidam gerações mesmo com ID igual. Metadados preservam controles. Seleção automática favorece padrão ativo e informa fallback.
- Mixer retém pedidos de reconciliação durante uma leitura. Preferências de sessão seguem indisponíveis até comprovação de identidade durável. Medidores contínuos por sessão seguem pendentes da avaliação de ciclo de vida/custo.
- Build com zero erros/avisos e 174 testes aprovados, incluindo os 101 anteriores. Integrações isoladas de reinício e preservação de arquivos aprovadas. Restauração física, áudio ativo, hotplug, retomada, layout/DPI e Windows 11 continuam pendentes.

### 2026-11-12 — F1-I4, picos reais e recuperação de dispositivos

- Repositório sincronizado até b4a6223 e executada a tarefa autorizada de NEXT_TASK.md, preservando os documentos normativos e a revisão.
- Barras reais de pico de reprodução/captura, percentual e dBFS, silêncio e indisponibilidade distintos, consulta aproximada de 10 Hz com pausa quando ocultas.
- Notificações públicas NAudio de adição, remoção, estado, padrões e propriedades relevantes, coalescência de rajadas, fallback de cinco segundos e recuperação na retomada.
- Worker MTA persistente proprietário de inscrições e medidores, snapshots por geração e descarte ordenado. Seleção explícita preservada e modo seguir padrão independente por direção.
- Mixer reconciliado por chave com preservação de foco/edição, eventos vinculados à geração e acesso isolado a sessões inacessíveis durante busca do alvo.
- Restore locked e build com zero erros/avisos. 101 testes aprovados, incluindo os 77 anteriores e seis integrações de leitura. Picos observados em zero nos endpoints e em cinco sessões. Executável iniciado e controles reconhecidos por acessibilidade.
- Medidor contínuo individual por sessão adiado para avaliação de ciclo de vida/custo. Resposta à fala/música, USB, padrões externos, suspensão, escritas, layout e Windows 11 continuam pendentes de ensaio físico.

### 2026-11-12 — F1-I3, mixer de aplicativos

- Sincronizadas NEXT_TASK.md e REVIEW.md até 4ab114d e executada F1-I3 mediante autorização do proprietário.
- Mixer de sessões reais por endpoint/instância, nome/processo quando disponível, PID auxiliar, estado, sons do sistema, volume e mute individuais.
- Reconciliação por polling, snapshots sem COM, comandos serializados e invalidação de seleção/instância antigas.
- Corrigida retenção de gerações do Incremento 2, com liberação após drenagem, confirmação dos sliders ao fim do gesto, rolagem da janela e proteção da restauração de balanço contra mudança externa detectável.
- Build com zero erros/avisos e 77 testes aprovados, incluindo os 56 anteriores. Integração de leitura encontrou quatro sessões reais e mediu custo de consultas. Escritas físicas e layout permanecem pendentes.
- Separados estado atual e histórico em PROJECT_STATUS.md. Incremento 4 aguarda orientação.

### 2026-11-12 — Fase 1, Incremento 2

- Publicado o Incremento 1 no commit 60238ea com a mensagem autorizada.
- Controles de volume 0–100%, mute independente, balanço estéreo condicional e centralização para os endpoints selecionados.
- Sincronização externa por polling em segundo plano, comandos associados a ID/revisão da seleção e cancelamento de comandos atrasados.
- Confirmação de suporte por leitura/releitura, mensagens de indisponibilidade, descarte de COM/callbacks e encerramento assíncrono das filas.
- Balanço com nível-base preservado, atenuação, validação de canais L/R e tentativa de restauração após atualização parcial.
- Restore/build aprovados, build final com zero erros/avisos e 56 testes aprovados. Leituras de hardware e controles da janela conferidos por acessibilidade. Escritas físicas e revisão visual pendentes.

O Incremento 2 recebeu aprovação provisória para publicação e revisão técnica, com commit/push autorizados. O Incremento 3 aguarda a revisão técnica e a configuração do fluxo de comunicação ChatGPT/Codex pelo GitHub.

### 2026-11-12 — Fase 1, Incremento 1

- Solução .slnx, SDK .NET 10 fixado, projetos Desktop/Audio/Tests x64 e referências.
- Dependência específica NAudio.Wasapi 3.1.0, xUnit v3 4.0.3 com MTP 2.5.2 e lock files dos três projetos.
- Janela pt-BR com seleção independente, padrões de multimídia/comunicações, estados, IDs e formato real do mix.
- Consultas serializadas em worker MTA, snapshots independentes de COM, descarte local, cancelamento e mensagens de indisponibilidade.
- README corrigido e instruções de execução/testes atualizadas.
- Build com zero erros/avisos, 16 testes aprovados e enumeração real de 67 endpoints. Executável iniciado e conteúdo da janela lido por acessibilidade. Avaliação visual/interativa e testes físicos adicionais pendentes.

Windows 10 permanece experimental. O Incremento 2 foi autorizado após a publicação desta entrega.

### 2026-11-12 — Fase 0

- Leitura das instruções e requisitos, inspeção de Git, SDK, sistema e estrutura.
- Documentação de arquitetura, suporte/limites, dependências e caminho preferido de integração global por endpoints virtuais.
- Plano incremental da v0.1.0 Alpha e critérios automatizados, físicos e de integração externa.
- Registro do estado real do projeto e navegação de documentação no README.

A v0.1.0 Alpha permanece planejada, com código, build e testes de produto pendentes.
