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
