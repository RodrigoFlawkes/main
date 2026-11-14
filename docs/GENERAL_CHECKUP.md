# Check-up geral — 14/11/2026

Base de código af2ba4b, planejamento c8e11b7. Ver registro detalhado em [REVIEW.md](../REVIEW.md), seção Check-up geral pós EQ-PREVIEW-1. Auditoria estática direcionada; não é execução independente de testes, auditoria completa de segurança ou teste de hardware.

| Área | Resultado / decisão | Prioridade |
| --- | --- | --- |
| EQ curto | Biquads reais, estado/clip preservados; headroom muito conservador, otimizar após integração/limiter | Posterior |
| Captura/reprodução | Ramo de completion com erro + descarte pode pular liberação; cleanup pré-Start e exceção terminal | CHECKUP-1 |
| Fechamento | async void sem proteção final pode falhar; confirmar com teste e tratar sem destruir stream ativo | CHECKUP-1 |
| Motor | FIR/SPSC/clock simulado e testes existentes; preservar, não reescrever | Preservar |
| Backend isolado | Somente fake; snapshot/priming/watermark exigem validação nativa | Antes de F2-I6B |
| Clock nativo | NAudio não preserva S_FALSE; ponte pública e mesma sessão | F2-I6A |
| Mixer Windows / preferências | Preservar controle de gerações, confirmação e JSON/backup; sem necessidade comprovada de refactor | Preservar |
| Integração externa | Ainda não provada; não multiplicar efeitos antes de F3 | Bloqueador |
| Hardware | A01–A13, Windows 11, layout e causa histórica de intermitência pendentes | Validação |
| Escopo | Plano mestre completo, Config para aparência/animações/sistema, apoiar rodapé | Preservar |
| Cota | 47% informados, Alto padrão, etapas curtas; nunca economizar sacrificando qualidade | Contínua |

Nenhuma função removida ou alteração de código neste commit. Correção prioritária em NEXT_TASK, seguida de preparação nativa e prova externa. Testes 495 por configuração são resultados relatados pelo Codex. Não iniciar streams físicos contínuos nem instalar drivers sem autorização/revisão. Não prometer tempo, cota ou compatibilidade universal.

## Execução CHECKUP-1 — correções e limites

Registro da implementação, separado da auditoria estática acima; resultados finais no PROJECT_STATUS.

- Reproduzidos nove casos antes da correção: oito captura/reprodução × descarte/seleção/invalidação/close deixavam Busy true após completion com erro, e um cancelamento pré-Start deixava modo/provider referenciados. Cleanup agora independe do erro prévio, é tentado uma vez por recurso concluído e preserva erro primário/erro de liberação separadamente. Cancelamento antes de Start limpa provider/modo/contadores, mantendo original quando aplicável.
- Engine usa época de entrega, invalida antes de Stop/falha, sela o clip e bloqueia novas operações enquanto há recursos ativos. Falha de Start pode ter iniciado recurso no contrato genérico: retenção até Completed. Nos wrappers novos/one-shot NAudio3.1.0, falha síncrona de Start/Play é reconhecida pelo caminho auditado (inicialização síncrona e Thread.Start final); erros assíncronos continuam em RecordingStopped/PlaybackStopped. Nenhuma API privada/reflection ou novo stream foi usado.
- Corrigida também a mensagem de Clear com stream ativo: não informa memória apagada/parada concluída antes de completion. Caso reproduzido por teste; mostra descarte pendente enquanto retém o recurso.
- RequestStop e erros de Tick simulados não matam o worker. Há polling de recuperação no controle, recursos só são descartados após completion; erro de Dispose de recurso já concluído é informado, sem segunda tentativa cega. DisposeAsync aguarda até 3 s por chamada; timeout não mata thread nem confirma parada e permite nova espera/retry. Chamada nativa síncrona bloqueada não pode ser abortada por esse prazo; referências/worker são preservados e nenhuma nova operação começa.
- FormClosing tem proteção final de async void e tarefas de cleanup independentes. Operações em voo/sucesso são reutilizadas; só as falhadas são tentadas novamente. Espera global padrão 5 s, status não modal e janela/caption disponíveis para novo fechar, conteúdo bloqueado. Não confirma fechamento enquanto algum componente falha/está pendente. Testes STA: falha/timeout do mic e callback de aviso de armazenamento que lança, comprovando cleanup independente e retry. Não reescrevemos os demais workers.
- Snapshot nulo/falho no transporte fake resulta em SnapshotUnavailable, gate inválido e média indisponível. Telemetria marca BufferSnapshotAvailable=false e conserva apenas último valor conhecido da mesma geração, sem tratá-lo como leitura atual. Geração inicial não tem snapshot. Priming/start foram verificados por callbacks fake durante Start: gate fechado recusa produção/consumo/submit e mantém priming. Isso **não** comprova sequência WASAPI ativa.
- Watermark continua contrato exclusivo do fake. F2-I6A deve tratar ponte pública IAudioClock/HRESULT S_OK/S_FALSE na mesma sessão e preparação exata sem Start. F2-I6B depende de revisão/autorização, guard realmente compartilhado com diagnóstico, ownership/quiescência/priming nativos e checkpoint real. Não foi conectada captura dupla, monitoramento ou rota externa.

DSP/EQ/headroom, FIR/nominal/adaptativo, controles Windows, preferências/backup, identidade, dependências e planejamento mestre preservados. A01–A13, testes de USB/suspensão/layout e Windows 11 continuam pendentes; Windows 10 experimental. Nenhum áudio físico iniciado automaticamente ou causa histórica do runner declarada resolvida. Publicação somente local autorizada, sem instalador/release pública; parar após CHECKUP-1.

Resultados finais:519 testes aprovados por configuração (495 anteriores+24 novos),0 falhas/ignorados, builds Debug/Release x64 com0 erros/avisos e restore locked auditado. Startup/close próprio da prévia normal/código0; pacote anterior preservado. Hashes/tempos/diagnósticos em PROJECT_STATUS. Não é nova aceitação independente da auditoria ou de hardware.
