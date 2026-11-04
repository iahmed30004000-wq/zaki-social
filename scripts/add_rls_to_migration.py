#!/usr/bin/env python3
"""يضيف تلقائياً تفعيل عزل الصفوف (enable_tenant_rls) لكل جدول جديد فيه workspace_id داخل ملف هجرة مولّد.

الاستعمال: python3 scripts/add_rls_to_migration.py backend/src/ZakiSocial.Data/Migrations/<ts>_Name.cs
لو الهجرة ما فيها جداول جديدة بعمود workspace_id ما بيغيّر شي.
"""
import re
import sys

path = sys.argv[1]
src = open(path, encoding="utf8", newline="").read()
nl = "\r\n" if "\r\n" in src else "\n"
text = src.replace("\r\n", "\n")

tables = []
for m in re.finditer(r'migrationBuilder\.CreateTable\(\s*name: "([a-z_]+)",(.*?)constraints: table =>', text, re.S):
    if "workspace_id = table.Column" in m.group(2):
        tables.append(m.group(1))

if not tables:
    print("no tenant tables in", path)
    sys.exit(0)

already = "enable_tenant_rls" in text
if already:
    print("rls already present")
    sys.exit(0)

down = text.index("protected override void Down(")
up_end = text.rindex("}", 0, down)  # قوس نهاية دالة Up
lines = "\n            // عزل المساحات: كل جدول فيه workspace_id لازم يكون عليه سياسة (دالة enable_tenant_rls من هجرة AuthTablesRls)\n"
for t in tables:
    lines += f'            migrationBuilder.Sql("SELECT enable_tenant_rls(\'{t}\');");\n'
text = text[:up_end].rstrip() + "\n" + lines + "        " + text[up_end:]
open(path, "w", encoding="utf8", newline="").write(text.replace("\n", nl))
print("rls added for:", ", ".join(tables))
