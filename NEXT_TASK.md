# Wave Studio — próxima etapa

ID: COMENTARIOS-PTBR — padronização dos comentários próprios em português brasileiro
Base técnica: f72304ebd6061a008c5b4e04a526fb7c0efa19a2. Prova 4 registrada em 54e3c1ce913a4c01654a5f2709ae190976e2ac8b e revisada em REVIEW.md. Referência: 737 testes comuns por configuração, 214 direcionados WASAPI e 2 Explicit excluídos. Esforço Alto. A prova 4 silenciosa está aprovada dentro de seu escopo; não repetir.

## Preparação

Ler AGENTS.md, REVIEW.md, PROJECT_STATUS.md, este arquivo, docs/REQUIREMENTS.md e docs/ARCHITECTURE.md. Conferir raiz oficial, git status, HEAD/origin/main; sincronizar somente fast-forward seguro. Preservar alterações locais, backups, artefatos e identidade. Se árvore suja ou divergência impedir sincronização segura, parar e reportar sem reset/clean/stash automático. Não iniciar a etapa sem autorização do proprietário.

## Escopo exclusivamente textual

Inventariar comentários explicativos próprios em todos os arquivos versionados de código, testes, scripts e configurações (src, tests, scripts e arquivos pertinentes na raiz), inclusive comentários de linha/bloco e documentação XML. Traduzir os que estiverem em outro idioma para português brasileiro claro e técnico; revisar consistência dos existentes sem reformatação generalizada. Preservar identificadores, nomes de APIs, referências XML (cref, param name etc.), comandos, exemplos literais, termos técnicos estabelecidos, diretivas, licenças, conteúdo de terceiros e arquivos gerados. Em PowerShell/configs preservar metadados, diretivas e sintaxe de comentários estruturados. Não traduzir strings funcionais, mensagens/códigos de diagnóstico, chaves, contratos de serialização ou conteúdo que possa afetar execução. Não alterar fins de linha/codificação sem necessidade. Em caso de dúvida semântica, preservar e reportar.

Nenhuma alteração de lógica, comportamento, algoritmos, parâmetros, tolerâncias, código executável, testes/asserções, UI, dependências, marca, configurações do Windows, driver ou política Restricted. Não instalar ferramentas/dependências adicionais para tradução sem autorização. Não excluir arquivos/backups. Não alterar NEXT_TASK.md ou REVIEW.md. Não iniciar F2-I6B2, F3, redesign ou internacionalização.

## Validação obrigatória

Comparar antes/depois tokens/sintaxe e strings funcionais (C# com análise sintática adequada e preservação de diretivas e estrutura XML; PowerShell com parser/tokens e preservação de diretivas/metadados; configs segundo o formato). Demonstrar que cada alteração de código é somente comentário próprio; não confiar apenas no diff textual nem usar substituições cegas. Reportar método, arquivos cobertos e exceções; se equivalência não puder ser demonstrada, parar antes de publicar. Conferir git diff --check e diff completo.

Restore locked com auditoria NuGet; builds Debug/Release x64; regressões completas comuns de ambas configurações com runner .NET direto e watchdog/protocolo já estabelecido (política Restricted, sem Explicit). Referência 737 aprovados e 2 Explicit excluídos por configuração, zero falhas e builds sem avisos/erros. Registrar comandos, resultados, duração e logs reais. Não executar Start físico, captura/reprodução, prova 4 ou outros Explicit. Se falhas intermitentes de alocação (EQ Debug 6.040 bytes / resampler nominal Release 3.992 bytes) voltarem, preservar evidência, distinguir resultados iniciais de diagnósticos e parar para revisão se houver falha não resolvida; não enfraquecer asserções, ajustar tolerâncias ou ocultar resultados. Não usar esta etapa para corrigir bugs funcionais.

## Registro e saída

Atualizar PROJECT_STATUS.md e CHANGELOG.md com inventário e resultados efetivos; README.md apenas se necessário para refletir o estado. Relatar arquivos examinados/modificados, quantidade de comentários traduzidos (ou método de contagem), exceções justificadas, equivalência, builds/testes, pendências, diff e estado da branch. Preservar históricos, sete aprovações manuais por relato e evidências da prova 4. Após validação satisfatória, commit/push exclusivamente deste checkpoint se expressamente autorizado pelo proprietário, sem force push. Sugestão: docs: translate project code comments to Brazilian Portuguese. Informar hash/link. Encerrar para revisão.

## Próximas etapas (não executar agora)

F2-I6B2: planejar checkpoints nativos coerentes, média temporal e calibração de granularidade para sincronização/controle de drift físico, considerando underruns registrados. Depende de revisão do checkpoint e escopo/autorização próprios; qualquer Start físico exige autorização específica. Depois concluir validações pertinentes da Fase 2 e prova externa F3. Internacionalização Config → Idioma obrigatória na primeira UI pós-F3, recursos por cultura, pt-BR padrão, seguir Windows, persistência, fallback e expansão de traduções reais conforme REQUIREMENTS. Não antecipar efeitos em massa nem redesign. Comentários novos próprios em pt-BR desde já.
