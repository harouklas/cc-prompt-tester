# Security policy

## Reporting a vulnerability

Please use GitHub's private vulnerability-reporting feature for this repository. Do not open a public issue containing API keys, document contents, local paths, customer data, or other sensitive information.

Include the affected version, reproduction steps, expected impact, and any suggested mitigation. Remove or replace all real credentials and document data before attaching logs or screenshots.

## Data-handling boundary

Prompt Tester is a local desktop client, but document extraction is not offline:

- The user supplies an OpenAI API key at runtime; the application keeps it in memory and does not write it to profiles, reports, or logs.
- Documents selected by the user are transmitted to the OpenAI Responses API.
- API requests set `store: false`.
- Excel reports and decision logs can contain document values, evidence, response metadata, and local paths.

Users are responsible for confirming that they are permitted to process each document and for protecting generated reports and logs.

## Public-repository hygiene

Never commit saved profiles, `.env`, API keys, customer prompts, source documents, reports, decision logs, or internal filesystem paths. `Profiles/Default.json` must remain synthetic.
