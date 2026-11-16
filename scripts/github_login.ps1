# Inicia sesión en GitHub CLI con el navegador y configura Git para usarla.
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git
gh auth status
