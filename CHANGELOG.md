# Changelog

## Em desenvolvimento

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
