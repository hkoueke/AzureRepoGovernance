# Auditeur de gouvernance des dépôts Azure DevOps

Outil en ligne de commande, **en lecture seule**, qui analyse les dépôts Git d'un
serveur **Azure DevOps Server 2022.2** (on-premises, intégré à l'Active Directory)
et produit un rapport Markdown consolidé.

L'outil ne modifie **jamais** le serveur : aucune suppression de branche, aucun
verrouillage, aucune modification de policy, aucune complétion de pull request.

## Prérequis

- .NET SDK 8.0
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
