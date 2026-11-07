# Security policy

Kimlik handles passwords, keys and tokens, so security reports are taken seriously and handled before anything else.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub: on the repository's **Security** tab, choose **Report a vulnerability** ([direct link](https://github.com/motorkiran/kimlik/security/advisories/new)). Please do not open a public issue, pull request or discussion about it.

Include what you can:

- the affected version or commit, and the configuration that matters;
- the steps or a proof of concept that shows the problem;
- what an attacker gains.

You will get an answer as soon as the report has been read, and are kept informed while it is fixed. Fixes are released, and described in the changelog and a GitHub security advisory, crediting you unless you prefer otherwise. Please give a reasonable time to fix the problem before you disclose it.

## Supported versions

Kimlik is at 0.x. Fixes go into the next release, built from the main branch; older releases are not patched. Upgrade to the latest release to get them.

## Scope

In scope: the Kimlik server and its hosted pages, admin panel and APIs, the SDKs in `sdk/`, the container image and the Helm chart.

Not vulnerabilities in Kimlik itself: problems that need an administrator's credentials or the master key, findings from automated scanners without a demonstrated impact, and missing hardening that [docs/deployment.md](docs/deployment.md) asks operators to apply, such as TLS or trusting forwarded headers only from the proxy.
