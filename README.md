# OrbitWeave

Lanceur radial pour Windows 10 et 11 : une roue posée sur le bureau, avec tes applis, sites, dossiers et enchaînements d'actions rangés en sections.

## Installer

1. Télécharge **OrbitWeave-…-Setup.exe** dans la [dernière version](https://github.com/gfloxe/OrbitWeave/releases/latest).
2. Lance-le. Pas besoin de droits administrateur ni d'installer .NET : tout est fourni.
3. La roue apparaît sur le bureau. Elle peut se lancer avec Windows (case cochée par défaut).

Windows SmartScreen peut afficher « Windows a protégé votre ordinateur » : l'installateur n'est pas signé. Clique sur « Informations complémentaires », puis « Exécuter quand même ».

## Mises à jour

OrbitWeave regarde au démarrage, puis toutes les 6 heures, si une version plus récente est publiée ici. Quand c'est le cas, **Plus de réglages… → Mise à jour → Mettre à jour** la télécharge, l'installe et relance la roue. Tes boutons, thèmes et réglages restent.

## Utilisation

- **Ouvrir une section** : la survoler (ou cliquer, selon le réglage), puis cliquer sur un bouton.
- **Rond du milieu** : clic pour replier ou redéployer la roue ; clic droit pour **Modifier la roue**, **Ouvrir tout l'arbre**, **Thèmes et réglages** ou **Quitter**.
- **Mode Modifier** : renommer, déplacer, ajouter des boutons, des colonnes et des sections, changer les icônes, définir ce que fait chaque bouton. Glisser un fichier ou une appli sur un bouton le remplace ou l'ajoute.
- **Raccourci** : Ctrl + Alt + W par défaut, modifiable dans les réglages.
- **Icône près de l'horloge** : thèmes, transparence, vitesse, export et import des données.

Les données sont dans `%APPDATA%\OrbitWeave`.

## Construire depuis le code

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download).

```powershell
dotnet build OrbitWeave.csproj -c Release
dotnet test OrbitWeave.Tests
```

La copie compilée se lance depuis `bin\Release\net10.0-windows\OrbitWeave.exe`. Seule la version installée se met à jour toute seule.
