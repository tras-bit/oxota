#!/usr/bin/env python3
"""Проверка C#-кода «Samsar» без Unity: синтаксис + обращения к членам типов.

Зачем: Unity в песочнице нет, а опечатка вроде `spec.widht` или ссылка на поле, которое
переименовали (`ui.previewPoint`), всплывает только при компиляции у игрока. Скрипт ловит
это заранее:

  1. синтаксические ошибки (дерево разбора tree-sitter на C#);
  2. `Тип.Член` — статические обращения к своим типам (TankSpec.Roster, GameSession.TankId, Fx.MakeDust…);
  3. `поле.Член` — обращения к компонентам через поля и локальные переменные своего типа.

Запуск:  python3 tools/checks/check_csharp.py          (из корня репозитория)
"""
import glob
import os
import sys
from collections import defaultdict

try:
    from tree_sitter import Language, Parser
    import tree_sitter_c_sharp as tscs
except ImportError:
    sys.exit("нет tree-sitter: pip install tree_sitter tree_sitter_c_sharp")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_DIRS = [os.path.join(ROOT, "game", "Assets", "Scripts"),
            os.path.join(ROOT, "game", "Assets", "Editor")]

# члены, которые могут прийти из базовых классов Unity — их отсутствие в нашем типе нормально
UNITY_MEMBERS = {
    "transform", "gameObject", "name", "tag", "enabled", "hideFlags", "GetComponent",
    "AddComponent", "GetComponentInChildren", "GetComponentsInChildren", "StartCoroutine",
    "StopCoroutine", "Invoke", "SendMessage", "Equals", "ToString", "GetHashCode", "GetType",
    "CompareTag", "isActiveAndEnabled", "TryGetComponent", "SetActive",
}

TYPE_KINDS = {"class_declaration", "struct_declaration", "enum_declaration",
              "interface_declaration", "record_declaration"}
MEMBER_KINDS = {"field_declaration", "property_declaration", "method_declaration",
                "constructor_declaration", "event_field_declaration",
                "event_declaration", "indexer_declaration", "delegate_declaration"}


def text(src, node):
    return src[node.start_byte:node.end_byte].decode("utf-8", "replace")


def child_of_kind(node, kind):
    for c in node.children:
        if c.type == kind:
            return c
    return None


def collect_types(src, node, out):
    """Собирает типы и их члены рекурсивно."""
    for child in node.children:
        if child.type not in TYPE_KINDS:
            collect_types(src, child, out)
            continue
        name_node = child_of_kind(child, "identifier")
        if name_node is None:
            continue
        tname = text(src, name_node)
        members = out.setdefault(tname, set())
        body = None
        for body_name in ("declaration_list", "enum_member_declaration_list", "body"):
            body = child_of_kind(child, body_name)
            if body is not None:
                break
        if body is not None:
            for m in body.children:
                if m.type == "enum_member_declaration":
                    ids = [c for c in m.children if c.type == "identifier"]
                    if ids:
                        members.add(text(src, ids[0]))
                    continue
                if m.type not in MEMBER_KINDS:
                    continue
                if m.type in ("field_declaration", "event_field_declaration"):
                    # `public static readonly TankSpec[] Roster = ...;` → ищем variable_declarator
                    for vd in _walk(m):
                        if vd.type == "variable_declarator":
                            ids = [c for c in vd.children if c.type == "identifier"]
                            if ids:
                                members.add(text(src, ids[0]))
                else:
                    nm = _member_name(src, m)
                    if nm:
                        members.add(nm)
        collect_types(src, child, out)


def collect_returns(src, node, out):
    """Возвращаемый тип методов: out[(ИмяТипа, ИмяМетода)] = набор типов («void», «ParticleSystem»…).
    Нужно, чтобы ловить «присваивание результата void-метода» — ошибка компиляции,
    которую иначе видно только в редакторе."""
    for child in node.children:
        if child.type in TYPE_KINDS:
            name_node = child_of_kind(child, "identifier")
            if name_node is not None:
                tname = text(src, name_node)
                for m in _walk(child):
                    if m.type != "method_declaration":
                        continue
                    nm = _member_name(src, m)
                    if not nm:
                        continue
                    # тип возврата — то, что стоит перед именем метода
                    idx = None
                    for i, c in enumerate(m.children):
                        if c.type == "identifier" and text(src, c) == nm:
                            idx = i
                            break
                    if idx is None:
                        continue
                    ret = " ".join(text(src, c) for c in m.children[:idx]).strip()
                    ret = ret.split()[-1] if ret else ""
                    if ret:
                        out.setdefault((tname, nm), set()).add(ret.split("<")[0])
        collect_returns(src, child, out)


def _member_name(src, node):
    """Имя метода/свойства/события: identifier перед списком параметров или аксессоров
    (а не тип возвращаемого значения — на этом легко ошибиться)."""
    stops = ("parameter_list", "accessor_list", "arrow_expression_clause", "equals_value_clause",
             "init", "body")
    idx = None
    for i, c in enumerate(node.children):
        if c.type in stops:
            idx = i
            break
    seq = node.children[:idx] if idx is not None else node.children
    for c in reversed(seq):
        if c.type == "identifier":
            return text(src, c)
        if c.type == "generic_name":
            ids = [x for x in c.children if x.type == "identifier"]
            if ids:
                return text(src, ids[0])
    return None


def class_of(node):
    p = node.parent
    while p is not None:
        if p.type in TYPE_KINDS:
            return p
        p = p.parent
    return None


def collect_extensions(src, node, out):
    """Extension-методы (`static X M(this Y y)`) — законные вызовы вида obj.M()."""
    for child in node.children:
        if child.type == "method_declaration":
            params = child_of_kind(child, "parameter_list")
            if params is not None and any(p.type == "parameter" and
                                          any(c2.type == "modifier" and
                                              text(src, c2).strip() == "this"
                                              for c2 in p.children)
                                          for p in params.children):
                nm = _member_name(src, child)
                if nm:
                    out.add(nm)
        collect_extensions(src, child, out)


def run():
    files = []
    for d in SRC_DIRS:
        files += sorted(glob.glob(os.path.join(d, "**", "*.cs"), recursive=True))
    if not files:
        sys.exit("не нашёл C#-файлы: проверь путь")

    parser = Parser(Language(tscs.language()))
    trees, types = {}, defaultdict(set)
    returns = {}
    EXTENSION_METHODS = set()
    syntax_errors = 0
    for path in files:
        src = open(path, "rb").read()
        tree = parser.parse(src)
        trees[path] = (src, tree)
        if tree.root_node.has_error:
            syntax_errors += 1
            print("!! синтаксис:", os.path.relpath(path, ROOT))
            for err in _errors(tree.root_node):
                line = err.start_point[0] + 1
                print("     строка %d: %s" % (line, text(src, err).strip()[:90].replace("\n", " ")))
        collect_types(src, tree.root_node, types)
        collect_extensions(src, tree.root_node, EXTENSION_METHODS)
        collect_returns(src, tree.root_node, returns)

    static_checks = static_bad = 0
    member_checks = member_bad = 0
    void_checks = void_bad = 0
    problems = []

    for path, (src, tree) in trees.items():
        # 1. статические обращения Тип.Член
        # 2. обращения через поля/локальные переменные известного типа
        for node in _walk(tree.root_node):
            if node.type != "member_access_expression":
                continue
            if node.parent is not None and node.parent.type == "member_access_expression" \
                    and node.parent.children[0] is not node:
                continue          # это часть цепочки a.b.c — проверим внешнее звено
            parts = node.children
            if len(parts) < 3:
                continue
            left, right = parts[0], parts[2]
            if left.type != "identifier" or right.type not in ("identifier", "property_identifier"):
                continue
            lname, rname = text(src, left), text(src, right)
            if lname in types and rname not in UNITY_MEMBERS:
                static_checks += 1
                if rname not in types[lname]:
                    static_bad += 1
                    problems.append("%s:%d  %s.%s — нет такого члена у типа %s"
                                    % (os.path.relpath(path, ROOT), left.start_point[0] + 1,
                                       lname, rname, lname))
                continue
            decl_type = _declared_type(src, tree.root_node, lname)
            if decl_type and decl_type in types and rname not in UNITY_MEMBERS \
                    and rname not in EXTENSION_METHODS:
                member_checks += 1
                if rname not in types[decl_type]:
                    member_bad += 1
                    problems.append("%s:%d  %s.%s — у типа %s (%s) нет такого члена"
                                    % (os.path.relpath(path, ROOT), left.start_point[0] + 1,
                                       lname, rname, decl_type, lname))

    # 3. результат void-метода нельзя присваивать или возвращать
    for path, (src, tree) in trees.items():
        for node in _walk(tree.root_node):
            if node.type not in ("variable_declarator", "assignment_expression"):
                continue
            value = None
            if node.type == "variable_declarator":
                # у variable_declarator инициализатор — прямой потомок после «=»
                # (у полей бывает equals_value_clause — учитываем оба вида)
                for i, c in enumerate(node.children):
                    if c.type == "=" and i + 1 < len(node.children):
                        value = node.children[i + 1]
                        break
            else:
                value = node.children[2] if len(node.children) > 2 else None
            if value is None:
                continue
            call = None
            for c in _walk(value):
                if c.type == "invocation_expression":
                    call = c
                    break
            if call is None:
                continue
            fn = call.children[0] if call.children else None
            if fn is None or fn.type != "member_access_expression" or len(fn.children) < 3:
                continue
            left, right = fn.children[0], fn.children[2]
            if left.type != "identifier":
                continue
            key = (text(src, left), text(src, right))
            if key in returns and returns[key] == {"void"}:
                void_checks += 1
                void_bad += 1
                problems.append("%s:%d  результат void-метода %s.%s(...) присвоен — так нельзя"
                                % (os.path.relpath(path, ROOT), node.start_point[0] + 1, key[0], key[1]))
            else:
                void_checks += 1

    print("Файлов проверено: %d, с ошибками: %d" % (len(files), syntax_errors))
    print("типов: %d | проверено статических обращений: %d | проблем: %d"
          % (len(types), static_checks, static_bad))
    print("проверено обращений к компонентам: %d | проблем: %d" % (member_checks, member_bad))
    print("проверено присваиваний результата вызова: %d | проблем: %d" % (void_checks, void_bad))
    if problems:
        print("\nНайдено:")
        for p in problems:
            print("   " + p)
    return 1 if (syntax_errors or static_bad or member_bad or void_bad) else 0


def _errors(node):
    out = []
    if node.type == "ERROR" or node.is_missing:
        out.append(node)
    for c in node.children:
        out += _errors(c)
    return out[:6]


def _walk(node):
    yield node
    for c in node.children:
        yield from _walk(c)


def _declared_type(src, root, varname):
    """Тип переменной/поля по имени: ищем `Тип имя` в объявлениях всех типов."""
    for node in _walk(root):
        if node.type != "variable_declarator":
            continue
        ids = [c for c in node.children if c.type == "identifier"]
        if not ids or text(src, ids[0]) != varname:
            continue
        decl = node.parent
        if decl is None or decl.type not in ("field_declaration", "local_declaration_statement",
                                            "variable_declaration", "event_field_declaration"):
            continue
        tnode = child_of_kind(decl, "identifier") if decl.type == "variable_declaration" else None
        if tnode is None:
            # `private HUD hud = null;` → тип стоит до variable_declarator
            for c in decl.children:
                if c.type in ("predefined_type", "identifier", "generic_name", "qualified_name"):
                    tnode = c
                    break
        if tnode is not None:
            t = text(src, tnode).split("<")[0].strip()
            return t.split(".")[-1]
    return None


if __name__ == "__main__":
    sys.exit(run())
