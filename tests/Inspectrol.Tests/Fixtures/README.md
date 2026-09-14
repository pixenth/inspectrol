# Test fixtures

Trimmed excerpts of rd-inspector.ru pages saved on 2026-09-12, used only by the parser tests.

- `support-index.html`: the model list from https://www.rd-inspector.ru/support/
- `barracuda-firmware.html`: https://www.rd-inspector.ru/support/inspector-barracuda-obnovlenie-po/
- `barracuda-database.html`: https://www.rd-inspector.ru/support/inspector-barracuda-obnovlenie/

Only the markup the parsers read is kept: the support list, the page title, the description block with the
warnings and the download links. Scripts, styles, images, navigation and unused attributes are removed.
Quirks of the original markup, such as a link of the form `https:///inspector-update.me/...`, are kept on purpose
because the parsers must cope with them.

The page content belongs to rd-inspector.ru and is included only as test data.