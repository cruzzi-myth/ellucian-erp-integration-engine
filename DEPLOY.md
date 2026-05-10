# GitHub Deploy Guide

Everything is built. Follow these two steps to make the project live.

---

## Step 1 — Push the project repo to GitHub

Run these commands in Terminal from inside the project folder:

```bash
cd ~/Documents/Claude/Projects/Ellucian\ ERP\ Integration\ Engine\ Enterprise\ Paltform

# Initialize git
git init
git add .
git commit -m "feat: Ellucian ERP Integration Engine — enterprise multi-tenant API bridge"

# Create the repo on GitHub (requires GitHub CLI — install at https://cli.github.com if needed)
gh repo create ellucian-erp-integration-engine --public --description "Enterprise multi-tenant API bridge connecting Ellucian Colleague to 12 third-party systems. 500k+ daily transactions, sub-100ms p99, 200+ university institutions." --push --source .

# Enable GitHub Pages (serves architecture-diagram.html and portfolio-card.html as live URLs)
gh api repos/cruzzi-myth/ellucian-erp-integration-engine/pages \
  --method POST \
  --field source[branch]=main \
  --field source[path]=/
```

After push, these URLs go live within ~1 minute:
- **Repo**: https://github.com/cruzzi-myth/ellucian-erp-integration-engine
- **Portfolio card**: https://cruzzi-myth.github.io/ellucian-erp-integration-engine/portfolio-card.html
- **Architecture diagram**: https://cruzzi-myth.github.io/ellucian-erp-integration-engine/architecture-diagram.html

---

## Step 2 — Update Professional-portfolio to link here

Open your Professional-portfolio repo and find the Ellucian project card data in the JavaScript.  
Update the GitHub URL and add a "View Portfolio Card" link:

| Field | Value |
|---|---|
| GitHub URL | `https://github.com/cruzzi-myth/ellucian-erp-integration-engine` |
| Portfolio card | `https://cruzzi-myth.github.io/ellucian-erp-integration-engine/portfolio-card.html` |
| Architecture | `https://cruzzi-myth.github.io/ellucian-erp-integration-engine/architecture-diagram.html` |

Push the Professional-portfolio change and the project card's "View on GitHub →" button will be fully wired up.

---

## If you don't have the GitHub CLI

Create the repo manually at https://github.com/new:
- Name: `ellucian-erp-integration-engine`
- Description: *Enterprise multi-tenant API bridge connecting Ellucian Colleague to 12 third-party systems. 500k+ daily transactions.*
- Visibility: Public

Then push:
```bash
cd ~/Documents/Claude/Projects/Ellucian\ ERP\ Integration\ Engine\ Enterprise\ Paltform
git init
git add .
git commit -m "feat: Ellucian ERP Integration Engine — enterprise multi-tenant API bridge"
git remote add origin https://github.com/cruzzi-myth/ellucian-erp-integration-engine.git
git branch -M main
git push -u origin main
```

Then enable GitHub Pages: **repo Settings → Pages → Branch: main → / (root) → Save**
