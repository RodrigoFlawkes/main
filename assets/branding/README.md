# Identidade oficial — Wave Studio

Logo fornecida e aprovada pelo proprietário em 13/11/2026.

- `original/WaveStudio.png`: original RGBA de 1254 × 1254 pixels, preservado byte a byte. SHA-256 `A97D343F27C7033BBE06447AC5AED9B20585CD97C97A7EA5A7D7E3D90BCC4E11`.
- `icons/WaveStudio.ico`: sete quadros PNG RGBA de 16, 24, 32, 48, 64, 128 e 256 pixels, também disponíveis como PNGs individuais.
- Derivados reduzidos com interpolação bicúbica de alta qualidade, mantendo cores, alfa e composição completa, sem recorte, redesenho ou fundo acrescentado. O original é a referência permanente, sem regeneração por IA.

Geração reproduzível no Windows por `scripts/Generate-BrandAssets.ps1`, usando System.Drawing sem dependências adicionais. Em terminal com scripts locais permitidos, execute `& ./scripts/Generate-BrandAssets.ps1`. Nenhuma política de segurança é alterada pelo script.

ApplicationIcon incorpora o ICO ao recurso Win32 do EXE. A janela usa uma cópia do ICO incorporado à assembly; o cabeçalho usa o PNG incorporado de 256 pixels em modo Zoom. Os recursos são descartados com a janela. O aplicativo não depende de arquivos externos de imagem.

Atalhos futuros: ícone de `WaveStudio.exe`, índice 0. Janela, taskbar e Alt+Tab recebem o ícone oficial através do aplicativo. Aparência final, atalhos fixados e caches do shell aguardam conferência visual no sistema alvo; nenhum cache ou atalho foi modificado automaticamente.

Preparação futura: ICO para instalador/atalhos; PNGs para download, Releases e atualizações; original para peças de maior resolução. Não há instalador, release pública ou site publicado. Recursos de identidade são fontes versionadas; binários de compilação permanecem ignorados em artifacts/bin/obj.

Metadados: produto/título `Wave Studio`, assembly/executável `WaveStudio`, descrição de gerenciamento de áudio para Windows, versão informativa `0.1.0-alpha`, assembly/arquivo `0.1.0.0`, identificação `Wave-Universal-Studio/main` e URL do repositório. Company identifica a organização do projeto, sem afirmar razão social ou assinatura digital. A Alpha continua em desenvolvimento.

Autorização de uso como identidade concedida pelo proprietário. Não foi inferida licença para uso da marca por terceiros ou registro de marca. Política de distribuição pública do produto permanece pendente.
