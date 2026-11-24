## Resumo

<!-- Descreva brevemente o que este PR muda e por quê. -->

## Issue relacionada

<!-- Ex.: Fecha #123 ou Relacionado a #123 -->

## Tipo de mudança

- [ ] Correção de bug
- [ ] Nova funcionalidade
- [ ] Refatoração / melhoria interna
- [ ] Documentação / tradução
- [ ] CI / automação / infraestrutura

## Validação executada

<!-- Liste os comandos realmente executados e o resultado (sem afirmar testes manuais não realizados). -->

- [ ] `dotnet restore WaveStudio.slnx --locked-mode`
- [ ] `dotnet build WaveStudio.slnx -c Debug --no-restore -warnaserror`
- [ ] `dotnet build WaveStudio.slnx -c Release --no-restore -warnaserror`
- [ ] `dotnet test` (regressão comum, com `--explicit off`)
- [ ] Não aplicável (mudança apenas de documentação/templates)

## Checklist do projeto

- [ ] Comentários explicativos e documentação XML escritos em português brasileiro (`pt-BR`)
- [ ] Se alterou textos da interface, as traduções foram atualizadas em `pt-BR`, `en` e `es`
- [ ] Nenhum recurso fictício ou não implementado foi exposto como funcional
- [ ] Nenhum teste `Explicit` de hardware físico foi disparado sem consentimento
- [ ] Documentação relevante atualizada (`PROJECT_STATUS.md`, `CHANGELOG.md`, `docs/`), se aplicável
