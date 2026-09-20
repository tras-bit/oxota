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
import re
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
# API, которых нет в Unity 2022.3.62f2 — в проект такого попадать не должно
NEWER_UNITY_API = (
    "FindObjectsByType", "FindFirstObjectByType", "FindAnyObjectByType", "Object.FindAnyObject",
    "AudioRandomContainer", "Awaitable", "Physics.CapsuleCastNonAlloc2", "RenderMode.WorldSpace",
    "EditorApplication.QueuePlayerLoopUpdateNow", "GraphicsFormat.R8G8B8A8_SRGB_Packed",
)

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


NUMERIC = ("int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
           "float", "double", "decimal")


def param_type_name(src, prm):
    """Имя типа параметра: bool / int / float / TankSpec / Vector3 …"""
    for c in prm.children:
        if c.type in ("predefined_type", "identifier", "qualified_name", "generic_name"):
            return text(src, c).split("<")[0].split(".")[-1].strip()
    return ""


def literal_kind(src, arg):
    """Вид литерала в аргументе: число / логическое / строка / иначе None."""
    for c in arg.children:
        if c.type in ("integer_literal", "real_literal"):
            return "number"
        if c.type == "boolean_literal":
            return "bool"
        if c.type in ("string_literal", "interpolated_string_expression", "character_literal"):
            return "string"
        if c.type in ("identifier", "invocation_expression", "member_access_expression",
                      "binary_expression", "parenthesized_expression", "object_creation_expression",
                      "cast_expression", "null_literal", "conditional_expression", "prefix_unary_expression",
                      "element_access_expression", "implicit_object_creation_expression"):
            return None
    return None


def collect_param_counts(src, node, out, types_out):
    """out[(ИмяТипа, ИмяМетода)] = набор вариантов числа параметров (перегрузки дают несколько)."""
    for child in node.children:
        if child.type in TYPE_KINDS:
            name_node = child_of_kind(child, "identifier")
            if name_node is not None:
                tname = text(src, name_node)
                for m in _walk(child):
                    if m.type != "method_declaration":
                        continue
                    nm = _member_name(src, m)
                    params = child_of_kind(m, "parameter_list")
                    if not nm or params is None:
                        continue
                    total = required = 0
                    types_row = []
                    for prm in params.children:
                        if prm.type != "parameter":
                            continue
                        total += 1
                        types_row.append(param_type_name(src, prm))
                        # параметр со значением по умолчанию можно не передавать
                        # (в грамматике это узел "=" внутри parameter)
                        has_default = any(c.type == "=" or c.type == "equals_value_clause"
                                          for c in prm.children)
                        if not has_default:
                            required += 1
                    out.setdefault((tname, nm), []).append((required, total))
                    types_out.setdefault((tname, nm), []).append(types_row)
        collect_param_counts(src, child, out, types_out)


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


def collect_nested_types(src, node, out, container=None):
    """Вложенные типы: имя -> имя контейнера. Из чужого файла ToastKind нужно писать HUD.ToastKind."""
    for child in node.children:
        if child.type in ("class_declaration", "struct_declaration",
                          "enum_declaration", "interface_declaration"):
            nm_node = child.child_by_field_name("name")
            nm = text(src, nm_node) if nm_node is not None else None
            if nm and container is not None and nm not in out:
                out[nm] = container
            if nm:
                collect_nested_types(src, child, out, nm)
        else:
            collect_nested_types(src, child, out, container)


SCOPE_NODES = ("block", "for_statement", "foreach_statement", "using_statement",
               "catch_clause", "switch_section")


def check_shadowing(src, tree, path, problems):
    """CS0136/CS0128: скрытие имён локальных и параметров внутри метода.
    В C# область видимости локальной — весь блок, даже ДО её объявления, поэтому
    «внутренний» halfW конфликтует и с объявленным ниже «внешним» halfW."""
    found = 0
    SCOPE = ("block", "for_statement", "foreach_statement", "using_statement",
             "catch_clause", "switch_section")

    def scope_of(n, method):
        p = n
        while p is not None and p is not method:
            if p.type in SCOPE:
                return p
            p = p.parent
        return method                      # параметры и «просто тело метода» живут здесь

    def ancestors(sc, method):
        out = []
        q = sc
        while q is not None and q is not method:
            q = q.parent
            while q is not None and q is not method and q.type not in SCOPE:
                q = q.parent
            if q is not None:
                out.append(q)
        if sc is not method:
            out.append(method)
        return out

    for method in _walk(tree.root_node):
        if method.type not in ("method_declaration", "constructor_declaration"):
            continue
        entries = []                       # (узел-область, имя, строка)

        def add(n, name):
            entries.append((scope_of(n, method), name, n.start_point[0] + 1))

        pl = method.child_by_field_name("parameters")
        if pl is not None:
            for prm in _walk(pl):
                if prm.type == "parameter":
                    nm = prm.child_by_field_name("name")
                    if nm is not None:
                        add(prm, text(src, nm))
        for node in _walk(method):
            if node.type == "variable_declarator":
                ids = [c for c in node.children if c.type == "identifier"]
                if ids:
                    add(node, text(src, ids[0]))

        # NB: у tree-sitter объекты-узлы не стабильны по идентичности (каждый .parent —
        # новая обёртка), поэтому сравниваем области по (тип, позиция), а не через is/id().
        def key(n):
            return (n.type, n.start_byte)

        flagged = set()
        for sc, name, line in entries:
            chain = {key(a) for a in ancestors(sc, method)}
            chain.add(key(sc))
            for sc2, name2, line2 in entries:
                if name2 == name and (key(sc2) != key(sc) or line2 != line) and key(sc2) in chain:
                    if (name, line) not in flagged:
                        flagged.add((name, line))
                        problems.append("%s:%d  %s — имя уже занято в этом или объемлющем блоке "
                                        "(CS0136/CS0128): в C# локальная видна всему блоку, даже до "
                                        "объявления; переименуй"
                                        % (os.path.relpath(path, ROOT), line, name))
                        found += 1
    return found


def run():
    files = []
    for d in SRC_DIRS:
        files += sorted(glob.glob(os.path.join(d, "**", "*.cs"), recursive=True))
    if not files:
        sys.exit("не нашёл C#-файлы: проверь путь")

    parser = Parser(Language(tscs.language()))
    trees, types = {}, defaultdict(set)
    returns = {}
    param_counts, param_types = {}, {}
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
        collect_param_counts(src, tree.root_node, param_counts, param_types)

    NESTED, NESTED_FILE = {}, {}
    for path, (src, tree) in trees.items():
        before = set(NESTED)
        collect_nested_types(src, tree.root_node, NESTED)
        for k in NESTED:
            if k not in before and k not in NESTED_FILE:
                NESTED_FILE[k] = path

    api_checks = api_bad = 0
    static_checks = static_bad = 0
    member_checks = member_bad = 0
    void_checks = void_bad = 0
    arg_checks = arg_bad = 0
    type_checks = type_bad = 0
    nested_checks = nested_bad = 0
    struct_checks = struct_bad = 0
    shadow_checks = shadow_bad = 0
    problems = []

    # CS0136/CS0128: скрытие имён локальных и параметров внутри метода
    for path, (src, tree) in trees.items():
        shadow_checks += 1
        shadow_bad += check_shadowing(src, tree, path, problems)

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

    # 4. число аргументов вызова должно совпадать с числом параметров метода
    for path, (src, tree) in trees.items():
        for node in _walk(tree.root_node):
            if node.type != "invocation_expression" or len(node.children) < 2:
                continue
            fn, args = node.children[0], node.children[1]
            if fn.type != "member_access_expression" or len(fn.children) < 3:
                continue
            left, right = fn.children[0], fn.children[2]
            if left.type != "identifier" or right.type != "identifier":
                continue
            key = (text(src, left), text(src, right))
            if key not in param_counts:
                continue
            passed = sum(1 for c in args.children if c.type == "argument")
            arg_checks += 1
            ok = any(req <= passed <= total for req, total in param_counts[key])
            if not ok:
                arg_bad += 1
                variants = " или ".join("%d..%d" % (req, total) if req != total else str(total)
                                        for req, total in sorted(set(param_counts[key])))
                problems.append("%s:%d  %s.%s(...) — передано аргументов %d, а метод ждёт %s"
                                % (os.path.relpath(path, ROOT), node.start_point[0] + 1,
                                   key[0], key[1], passed, variants))

    # 5. очевидные несовпадения типов аргументов-литералов (число там, где ждут bool, и наоборот)
    for path, (src, tree) in trees.items():
        for node in _walk(tree.root_node):
            if node.type != "invocation_expression" or len(node.children) < 2:
                continue
            fn, args = node.children[0], node.children[1]
            if fn.type != "member_access_expression" or len(fn.children) < 3:
                continue
            left, right = fn.children[0], fn.children[2]
            if left.type != "identifier" or right.type != "identifier":
                continue
            key = (text(src, left), text(src, right))
            if key not in param_types:
                continue
            arg_nodes = [c for c in args.children if c.type == "argument"]
            for variant, row in zip(param_counts[key], param_types[key]):
                if not (variant[0] <= len(arg_nodes) <= variant[1]):
                    continue
                for i, arg in enumerate(arg_nodes):
                    if i >= len(row):
                        break
                    kind, ptype = literal_kind(src, arg), row[i]
                    wrong = None
                    if ptype == "bool" and kind == "number":
                        wrong = "число вместо true/false"
                    elif ptype in NUMERIC and kind in ("bool", "string"):
                        wrong = ("true/false" if kind == "bool" else "строка") + " вместо числа"
                    if wrong:
                        type_bad += 1
                        problems.append("%s:%d  %s.%s(...), аргумент %d: %s (ждёт %s)"
                                        % (os.path.relpath(path, ROOT), arg.start_point[0] + 1,
                                           key[0], key[1], i + 1, wrong, ptype))
                    type_checks += 1
                break

    # 0. API новее целевой версии движка: в 2022.3 это не скомпилируется
    for path, (src, tree) in trees.items():
        for node in _walk(tree.root_node):
            if node.type not in ("identifier", "member_access_expression"):
                continue
            piece = text(src, node)
            for bad in NEWER_UNITY_API:
                if piece == bad or piece.endswith("." + bad):
                    api_checks += 1
                    api_bad += 1
                    problems.append("%s:%d  %s — этого API нет в Unity 2022.3"
                                    % (os.path.relpath(path, ROOT), node.start_point[0] + 1, piece))
                    break
            api_checks += 1

    # 7. вложенный тип без квалификации: ToastKind из другого файла — только HUD.ToastKind (CS0103)
    for path, (src, tree) in trees.items():
        rel = os.path.relpath(path, ROOT)
        s8 = src.decode("utf-8", errors="ignore")
        for name, container in NESTED.items():
            if NESTED_FILE.get(name) == path:
                continue
            # ловим только использование «Имя.член»: объявления методов и вызовы X.Имя() не трогаем
            for m in re.finditer(r"\b%s\s*\.(?!\.)" % re.escape(name), s8):
                before = s8[max(0, m.start() - len(container) - 4):m.start()]
                if re.search(r"\b%s\s*\.\s*\Z" % re.escape(container), before):
                    continue
                line_start = s8.rfind("\n", 0, m.start()) + 1
                if "//" in s8[line_start:m.start()]:
                    continue
                nested_checks += 1
                nested_bad += 1
                problems.append("%s:%d  %s — вложенный тип (объявлен внутри %s): из этого файла нужен префикс %s.%s"
                                % (rel, s8.count("\n", 0, m.start()) + 1, name, container, container, name))
                break

    # 8. CS1612: свойство структуры-возврата меняют напрямую: ps.main.x = ... — не скомпилируется
    STRUCT_PROPS = ("main", "emission", "shape", "collision", "colorOverLifetime",
                    "sizeOverLifetime", "velocityOverLifetime", "rotationOverLifetime",
                    "limitVelocityOverLifetime", "noise", "textureSheetAnimation",
                    "subEmitters", "trigger", "inheritVelocity")
    pat1612 = re.compile(r"\.\s*(%s)\s*\.\s*\w+\s*=(?!=)" % "|".join(STRUCT_PROPS))
    for path, (src, tree) in trees.items():
        rel = os.path.relpath(path, ROOT)
        s8 = src.decode("utf-8", errors="ignore")
        for m in pat1612.finditer(s8):
            line_start = s8.rfind("\n", 0, m.start()) + 1
            if "//" in s8[line_start:m.start()]:
                continue
            struct_checks += 1
            struct_bad += 1
            problems.append("%s:%d  %s — прямое изменение свойства структуры-возврата (CS1612): "
                            "сначала сохрани в переменную: var m = ps.main; m.x = ..."
                            % (rel, s8.count("\n", 0, m.start()) + 1, m.group(0).strip()))

    print("Файлов проверено: %d, с ошибками: %d" % (len(files), syntax_errors))
    print("проверено имён API на совместимость с 2022.3: %d, проблем: %d" % (api_checks, api_bad))
    print("типов: %d | проверено статических обращений: %d | проблем: %d"
          % (len(types), static_checks, static_bad))
    print("проверено обращений к компонентам: %d | проблем: %d" % (member_checks, member_bad))
    print("проверено присваиваний результата вызова: %d | проблем: %d" % (void_checks, void_bad))
    print("проверено вызовов с проверкой числа аргументов: %d | проблем: %d" % (arg_checks, arg_bad))
    print("проверено типов аргументов-литералов: %d | проблем: %d" % (type_checks, type_bad))
    print("проверено упоминаний вложенных типов: %d | проблем: %d" % (nested_checks, nested_bad))
    print("проверено изменений свойств структур-возвратов: %d | проблем: %d" % (struct_checks, struct_bad))
    print("проверено методов на скрытие имён (CS0136): %d | проблем: %d" % (shadow_checks, shadow_bad))
    if problems:
        print("\nНайдено:")
        for p in problems:
            print("   " + p)
    return 1 if (syntax_errors or static_bad or member_bad or void_bad or arg_bad
                or type_bad or api_bad or nested_bad or struct_bad or shadow_bad) else 0


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
