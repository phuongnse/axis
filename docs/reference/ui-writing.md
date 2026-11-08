# UI writing

This page says how Axis's own words are written. It covers Axis's screen texts: every text that Axis itself shows or
sends to a person, in every locale, on its screens, its sign-in and sign-up pages, its emails, and in the built-in
modules and applications that ship with it. It applies to every person or agent that adds or changes one. An author's
own texts, the samples' included, belong to that author, who may follow this page.

The terms each locale uses are in the [glossary](ui-glossary.json), and the terms that wait for the owner are in the
[proposed glossary](ui-glossary-proposed.json). This page says how to write; the glossary says which word names which
concept.

## Principles

These principles hold in every language.

- **Write for the purpose, not from the English.** Each locale is written, not translated. Start from what the text is
  for, where it shows and what the person does next, then write it as a native writer of that language would. English
  is the source of the keys, not of the sentences.
- **The person's words.** Name what the person sees and does, in the terms of the people who use the screen. Write
  "The record could not be saved", not the name of the API call that failed.
- **A setting by what it means.** Name a setting by what it is to the person, not by the English word for it: what
  people see as a field's name is its display name, and what a field holds is its data type.
- **One term for one concept.** Use the glossary's term. Never bring in a synonym, and never change an approved term in
  passing: a change of term is proposed in the proposed glossary, which the owner approves, and then made in every text
  that uses it.
- **Direct verbs for actions.** A button or a menu item says what it does, with a verb first where the language allows:
  Save, Create, Delete, Restore, Export data. A dialog's finish button repeats the verb of its title: "Delete the role?"
  ends with Delete, not OK.
- **No filler.** Leave out words that add no meaning, such as "please", "successfully", "in order to", or phrases that
  only wrap a verb, such as "perform the deletion of". Say "Delete".
- **Be specific.** Say what happened, to which thing, and what to do next. "Something went wrong" is only for a failure
  whose cause the screen does not know.
- **Keep meaning, severity and security.** A rewrite keeps the business meaning, how serious the message is, and what it
  must not reveal. A destructive action says what is lost. A sign-in failure gives no reason, in any language.
- **What stays in English is decided by group, never word by word.** Every word on a screen is in one of three groups:
  - _Names and acronyms_ never change: product names (Axis, Studio), acronyms and formats (JSON, API, ID, URL, CSV),
    diagnostic codes (`AXC0035`), names an author gave (a field or a resource name) and placeholders such as `{count}`.
  - _Ordinary words_ are always in the locale's language: Save, Cancel, Search, Name, Description, Required.
  - _Axis's concepts_, the terms of [concepts.md](../domain/concepts.md) such as entity, field, record, rule, process and
    release, are always written in the locale's language, by the glossary. The only exceptions are a closed list of
    integration and operations terms that the people who read them use in that form, such as webhook, endpoint, tenant
    and token. They show only on integration and operator screens, so an author's or a user's screen reads wholly in
    one language.

  A word joins the closed list only by a change of the glossary. One sentence never names a concept twice, once in each
  language.

- **Plain, polite and neutral.** Axis is software for companies: no slang, no jokes, no exclamation marks, and no
  familiar tone. Polite does not mean wordy. How a text addresses the person is each language's own rule, below.
- **Short.** A button is one to three words. A label is a noun phrase. A help text is one sentence, two at most. Leave
  room for languages that run longer: Vietnamese often takes a third more space than English.

## Kinds of text

Each kind of text has its own shape, the same on every screen:

| Kind               | How it reads                                                                  | Example (en)                                                                  |
| ------------------ | ----------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Button, menu item  | A verb and its object, no period. A button that opens a dialog ends with "…". | Add field, New entity…, Publish…                                              |
| Dialog title       | The question or the task, as the finish button names it.                      | Delete the role?                                                              |
| Field label        | A noun phrase, no period, no colon.                                           | Display field                                                                 |
| Help text          | One sentence that says what the field is for or what to enter, with a period. | The field that names a record in lists and pickers.                           |
| Placeholder        | Not used: what a placeholder would say goes into the help text.               |                                                                               |
| Validation message | What to do, as a short imperative, with a period.                             | Enter a name.                                                                 |
| Error message      | What happened, why if known, and what to do, with a period.                   | The entity could not be saved because its name is taken. Choose another name. |
| Empty state        | What is not there, then what to do.                                           | No fields yet. Add the first field.                                           |
| Confirmation       | What will happen and what is lost, then the verb on the button.               | The role and its grants are deleted. This cannot be undone.                   |
| Tooltip            | The name of an icon button, no "…", no period.                                | Filter                                                                        |
| Status             | A short state, as a noun or an adjective.                                     | Live, Not published                                                           |

A label, a title and a button take sentence case in English: "Display field", not "Display Field".

A label or a column title does not repeat what its section, table, row or dialog is about: the Fields table's columns
are Name, Label and Type, not Field name, Field label and Field type, and a step's settings say Name, not Step name. An
accessible name that no visible heading explains, such as an input's `aria-label` in a row with no title, keeps the
noun.

## Placeholders and numbers

A text that holds a value reads as a whole sentence in every locale.

- Keep every placeholder, such as `{count}` or `{entity}`, in every locale, and move it to where the language puts it.
- A word Axis defines that a placeholder puts in a sentence, such as a type's or a kind's name, follows the sentence's
  capitals: "Text settings" where it starts the text, "Thiết lập văn bản" in the middle. A name an author gave, such as
  an entity's label, keeps its own capitals: "Tham chiếu tới Phòng ban".
- A count reads naturally: English needs "1 field" and "3 fields"; a language without plural forms writes one text.
- Dates, times and numbers come from the locale's formats, never from words in a text.

## The glossary

[`ui-glossary.json`](ui-glossary.json) lists each concept of [concepts.md](../domain/concepts.md) that a screen names,
and the verbs of the common actions, with the term of each locale. It holds the `approved` terms, and the owner reviews
every change to it. [`ui-glossary-proposed.json`](ui-glossary-proposed.json) holds the terms that wait for the owner,
with the same fields. A term has a `status`:

- `approved`: the term to use, in `ui-glossary.json`;
- `proposed`: suggested, waiting for the owner, in `ui-glossary-proposed.json`;
- `decide`: the owner chooses among the `options` before any text changes, in `ui-glossary-proposed.json`.

A new term, or a change of term, goes in `ui-glossary-proposed.json`, which needs no owner review. The owner approves
it by moving it to `ui-glossary.json` with the status `approved`.

`keep`, in `ui-glossary.json`, lists what no locale translates, by group: `names` for names and acronyms, and
`integration` for the closed list of integration and operations terms.

The glossary and this page are conventions. They hold by being followed when a text is written and reviewed: the
glossary says which word to use for a concept, and lists no words to avoid, as no list of wrong words is ever complete.

## English

These rules add to the principles for the `en` texts.

- Plain international English, with British spelling where it differs: "behaviour", "licence" for the noun.
- Address the person as "you" only when a sentence needs a subject; most texts need none.
- An action is a verb: "Add field", not "Field addition".

## Vietnamese

The voice is the meaning rewritten as a Vietnamese writer would say it, in short, everyday, modern words.

- **"Bạn" when speaking to the person, no subject otherwise.** Most texts need no subject: "Nhập tên.", "Không lưu được
  bản ghi.". A question or advice addressed to the person says "bạn": "Bạn có muốn xóa vai trò này không?", "Bạn nên
  sao lưu trước khi khôi phục.". Axis speaking of itself says "chúng tôi", rarely. Never "người dùng" for the person
  reading, and never "quý khách", "mình" or a familiar tone.
- **The people who use an application are "người dùng", and are mostly left out.** Studio's help speaks of them often,
  as English says "people". Leave them out where the sentence needs no subject: "Thông báo hiện ở trường này khi điều
  kiện không thỏa.", "Tên hiện trong menu và biểu mẫu.". Where it needs one, "người dùng": "Người dùng chọn một người.".
  Never "mọi người" for them, which reads as a crowd.
- **Verb first for actions.** "Thêm trường", "Thêm vai trò…", not "Trường mới", "Vai trò mới…".
- **"Tạo" for an application, "Thêm" for a resource.** A button that makes a new application says "Tạo": "Tạo ứng
  dụng…". A button that makes a new resource of an application says "Thêm", as the resource joins the application:
  "Thêm đối tượng…", "Thêm tài nguyên…". English keeps "New application…" and "New entity…".
- **"Hãy" and "Vui lòng" only where they belong.** A validation message, a button or a tooltip takes the bare verb:
  "Nhập tên.", "Chọn loại.". A sentence of guidance may start with "Hãy": "Hãy đăng nhập lại để đổi vai trò.". "Vui lòng"
  is for a real request where English says "please", and is rare.
- **Error messages in one pattern.** Declarative, active, without "bị", ending with a period. The same meaning always
  takes the same phrase:

  | English                                        | Vietnamese          | Not                            |
  | ---------------------------------------------- | ------------------- | ------------------------------ |
  | Could not save…, Failed to save…, Cannot save… | Không lưu được…     | Lưu … thất bại, Không thể lưu… |
  | Cannot find…, Unable to find…, … not found     | Không tìm thấy…     | Không thể tìm thấy…            |
  | Not enough…, Insufficient…                     | Không đủ…           |                                |
  | An error occurred.                             | Có lỗi.             | Đã xảy ra lỗi.                 |
  | You do not have access to…                     | Bạn không có quyền… |                                |

  "Không thể" stays for what cannot be done at all, by rule, not for a failure: "Không thể xóa vai trò đang được dùng.".

- **Modern words over old or stiff ones.** "Không tải được", not "Tải về không thành công"; "Xin chờ", not "Vui lòng đợi";
  "ẩn", not "giấu"; "ID", not "mã định danh"; a fragment where it reads better: "Nếu còn sự cố", not "Nếu bạn vẫn còn
  sự cố".
- **Not the English sentence's shape.**
  - No "nó" for "it" or "its": name the thing again, or leave it out. "Hiện dữ liệu", not "Hiện dữ liệu của nó";
    "Không tài nguyên nào khác dùng tài nguyên này.", not "Không tài nguyên nào khác dùng nó."
  - No passive built on "được" or "bị" where English is passive: "Đã xóa vai trò", not "Vai trò đã được xóa".
  - No "việc", "thực hiện việc", "tiến hành" or "thao tác" wrapped around a verb: "Xóa", not "Thực hiện việc xóa".
  - No literal noun piles from English: "Độ rộng (trên 12 cột)", not "Độ rộng theo phần mười hai của hàng". A compound
    reads in Vietnamese order: "Tài khoản Internet", not "Internet tài khoản".
- **A short label that reads two ways on its own is rewritten.** "Nhãn trường" can read as a school's label; "Tên
  hiển thị" cannot.
- **An optional field says "không bắt buộc".** A label that marks a field as optional ends with "(không bắt buộc)",
  never "(tùy chọn)", which reads as a choice: "Ghi chú (không bắt buộc)" for "Note (optional)".
- **Capital on the first word only**, on buttons and titles too: "Chọn tất cả", not "Chọn Tất Cả". A proper name keeps its
  capitals: "Việt Nam".
- **One spelling and spacing.** The modern placement of tone marks: "xóa", "hủy", "hòa". No space before `. , : ; ? ! )`
  and one after; a non-breaking space between a number and its unit, "250 GB", and none before "%": "80%".
- **Acronyms stay in English**, ID, URL, PIN, JSON, and an English term the glossary keeps is not mixed with a
  translation of the same concept on one screen.

## Checks

A test checks the platform texts for the rules that a test can decide, and no list of words. It fails when:

- a key is missing in a locale, or a placeholder of the English text is missing or added in another locale;
- a text in another locale is the same as the English one, unless the glossary's `keep` allows it;
- a key is written more than once in one locale file;
- a concept is in both glossary files, or a term is in the glossary file that does not match its `status`.

`tests/Axis.Server.Tests/TextCatalogTests.cs` runs these checks in `scripts/test.sh`. A word of a text passes when a
one- or two-word `keep` entry names it, ignoring case. The keys that broke a rule before the check are listed by
key in `tests/Axis.Server.Tests/TextCatalogBaseline.json`. That list only goes down: a fixed key must be removed
from it, and a new break fails.

No test decides the words: a phrase that breaks a language's pattern, a synonym of a glossary term or a concept left in
English. NexKit's review checks them on every change to a screen text, as
[how pull requests merge](../delivery.md#how-pull-requests-merge) says.

## Changing a text

A change to a screen text takes these steps:

1. Read the key's place on the screen and its purpose, then write every locale of the change from that purpose.
2. A concept with no term yet, or a term to change, is added to `ui-glossary-proposed.json` as `proposed` in the same
   change, for the owner to approve by moving it to `ui-glossary.json`.
3. A change that rewrites many texts of one locale goes area by area, with the owner reviewing the first area before
   the rest follow.
