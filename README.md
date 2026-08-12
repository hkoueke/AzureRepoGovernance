# Auditeur de gouvernance des dépôts Azure DevOps

Outil en ligne de commande, **en lecture seule**, qui analyse les dépôts Git d'un
serveur **Azure DevOps Server 2022.2** (on-premises, intégré à l'Active Directory)
et produit un rapport Markdown consolidé.

L'outil ne modifie **jamais** le serveur : aucune suppression de branche, aucun
verrouillage, aucune modification de policy, aucune complétion de pull request.

## Prérequis

- .NET SDK 10.0
- Un poste **Windows joint au domaine** : l'authentification est celle de la session
  AD courante (NTLM/Kerberos). Aucun jeton, aucun mot de passe, aucun secret stocké.
- Des droits de lecture sur les projets à auditer (le serveur ne renvoie que ce que
  l'utilisateur a le droit de voir).

## Build et tests

```powershell
dotnet build   -c Release
dotnet test    -c Release
```

Les avertissements sont traités comme des erreurs (analyseurs Meziantou, AsyncFixer,
SonarAnalyzer inclus) : le code doit passer l'analyse statique pour compiler.
`NuGetAudit` fait par ailleurs échouer le restore si une dépendance, directe ou
transitive, est signalée vulnérable.

## Intégration et livraison continues

Le workflow `.github/workflows/ci.yml` s'exécute à chaque push sur `main`, à chaque
pull request, et une fois par semaine. Il enchaîne restore, build et tests, puis
publie un exécutable **auto-contenu et mono-fichier** pour `win-x64` et `win-x86`,
téléchargeable depuis l'onglet *Actions*.

L'exécution hebdomadaire existe pour `NuGetAudit` : réglé sur `all`/`low`, il fait
échouer le restore dès qu'un avis est publié sur une dépendance, **sans la moindre
modification de code**. Mieux vaut l'apprendre le lundi matin qu'au milieu d'une
pull request.

Pousser une étiquette `v*` crée en plus une release GitHub avec les deux
exécutables, leurs empreintes SHA256 et `appsettings.sample.json` :

```powershell
git tag v1.0.0
git push origin v1.0.0
```

### L'exécutable livré

Un seul fichier suffit : ni runtime .NET, ni DLL à côté. Comptez 35 à 45 Mo — le
runtime, les analyseurs de code exclus et les données ICU (l'outil n'est pas en
globalisation invariante) sont embarqués et compressés.

Sans `appsettings.json` à côté de lui, l'exécutable a besoin du serveur en ligne
de commande :

```powershell
GovernanceAuditor-win-x64.exe --serveur https://devops.entreprise.local --collection DefaultCollection
```

Déposez `appsettings.sample.json` renommé en `appsettings.json` à côté de
l'exécutable pour éviter de retaper ces options.

> **Environnements verrouillés.** Un exécutable mono-fichier extrait ses
> bibliothèques natives dans `%TEMP%` au premier lancement. Si une stratégie
> AppLocker interdit l'exécution depuis les dossiers temporaires, redirigez
> l'extraction : `setx DOTNET_BUNDLE_EXTRACT_TO_DIR C:\Outils\GovernanceAuditor\cache`.

Vérification de l'empreinte après téléchargement :

```powershell
Get-FileHash GovernanceAuditor-win-x64.exe -Algorithm SHA256
```

## Configuration

`appsettings.json` est lu **à côté de l'exécutable**. Réglages minimaux :

```json
{
  "AzureDevOpsServer": {
    "BaseUrl": "https://devops.entreprise.local",
    "Collection": "DefaultCollection"
  }
}
```

> **HTTPS est exigé.** Une URL `http://` est rejetée sauf à activer explicitement
> `AzureDevOpsServer:AllowInsecureHttp` : en authentification Windows, un canal en
> clair expose la négociation NTLM/Kerberos.

## Exécution

```powershell
GovernanceAuditor                              # tous les projets accessibles
GovernanceAuditor --projets Paie,Facturation   # périmètre restreint
GovernanceAuditor --sortie D:\audits           # dossier du rapport
GovernanceAuditor --anonymiser                 # pseudonymise les acteurs
GovernanceAuditor --aide                       # aide complète
```

Toute clé de configuration est surchargeable : `--Rules:RequiredReviewers 3`.

Le rapport est écrit dans `Reporting:OutputDirectory` sous le nom
`governance-report-yyyyMMdd.md`. Si le rapport du jour existe déjà, l'heure est
ajoutée au nom : une seconde exécution n'écrase jamais la précédente.

## Codes de sortie

| Code | Signification |
|---|---|
| `0` | Aucune anomalie critique |
| `1` | Au moins une anomalie critique, ou erreur fatale |
| `2` | Configuration invalide (rien n'a été analysé) |
| `3` | Analyse partielle : trop de dépôts en échec, ou interruption |

Un résultat partiel prime sur les anomalies détectées : une analyse incomplète ne
permet pas de conclure à l'absence de problème.

## Confidentialité

Le rapport contient par défaut des **données personnelles** (noms et adresses e-mail
des auteurs). Traitez le dossier de sortie comme une ressource à accès restreint, ou
activez `Privacy:RedactActors` (`--anonymiser`), qui remplace les acteurs par des
pseudonymes stables au sein d'une exécution.

## Structure

| Projet | Rôle |
|---|---|
| `GovernanceAuditor.Core` | Modèle de domaine, six analyseurs, contrats, options. Aucune I/O. |
| `GovernanceAuditor.Infrastructure.AzureDevOps` | Client REST 7.1, pagination, mapping. Aucune logique métier. |
| `GovernanceAuditor.Reporting` | Génération Markdown déterministe. |
| `GovernanceAuditor.Console` | Hôte, configuration, orchestration, rendu console. |
| `GovernanceAuditor.Tests` | Tests unitaires et d'architecture. |
