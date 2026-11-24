# Política de Segurança

O **Wave Studio** realiza processamento local de áudio no Windows, sem telemetria de áudio e sem envio de gravações para serviços externos.

## Princípios de segurança do projeto

- Não coletar, gravar ou transmitir áudio sem ação explícita do usuário.
- Não instalar drivers não assinados, desativar Secure Boot ou alterar integridade de memória do Windows.
- Não alterar dispositivo padrão ou volume master do Windows silenciosamente.
- Manter dependências auditadas (`NuGetAudit=true` com `WarningsAsErrors=NU1900` no CI).

## Como relatar uma vulnerabilidade

Se você encontrar uma vulnerabilidade de segurança, problema de privacidade (como captação indevida de microfone/loopback) ou risco em dependências:

1. **Não** publique detalhes exploráveis em uma issue pública antes da análise.
2. Use a aba **Security → Advisories** do repositório no GitHub (caso habilitada) ou entre em contato diretamente com o mantenedor pelo perfil do repositório.
3. Inclua versão do Windows, commit/branch afetado, passos para reproduzir e impacto estimado.