# UI writing

How the words of Axis's own screens are written: Studio, the operator console, the shell, the sign-in pages and the
platform's messages. It covers the platform texts in `src/Axis.Presentation/Texts/<locale>.json`, in every locale, and
every person or agent that adds or changes one. An application's own texts belong to its author, who may follow it.

The terms each locale uses are in the [glossary](ui-glossary.json). This page says how to write; the glossary says which
word names which concept.

## Principles

These hold in every language.

- **Write for the purpose, not from the English.** Each locale is written, not translated. Start from what the text is
  for, where it shows and what the person does next, then write it as a native writer of that language would. English
  is the source of the keys, not of the sentences.
- **The person's words.** Name what the person sees and does, in the terms of the people who use the screen. Write
  "The record could not be saved", not the name of the API call that failed.
- **One term for one concept.** Use the glossary's term. Never bring in a synonym, and never change an approved term in
  passing: a change of term is a change of the glossary, which the owner approves, and then of every text that uses it.
- **Direct verbs for actions.** A button or a menu item says what it does, with a verb first where the language allows:
  Save, Create, Delete, Restore, Export data. A dialog's finish button repeats the verb of its title: "Delete the role?"
  ends with Delete, not OK.
- **No filler.** Leave out words that add no meaning, such as "please", "successfully", "in order to", or phrases that
  only wrap a verb, such as "perform the deletion of". Say "Delete".
- **Be specific.** Say what happened, to which thing, and what to do next. "Something went wrong" is only for a failure
  whose cause the screen does not know.
- **Keep meaning, severity and security.** A rewrite keeps the business meaning, how serious the message is, and what it
  must not reveal. A destructive action says what is lost. A sign-in failure gives no reason, in any language.
- **Do not translate names.** Product names (Axis, Studio), technical names (JSON, API, ID, URL, CSV, XLSX, webhook),
  diagnostic codes (`AXC0035`), names an author gave (a field name, a resource name) and placeholders such as `{count}`
  stay as they are. The glossary's `keep` list holds them.
- **Plain, polite and neutral.** Axis is software for companies: no slang, no jokes, no exclamation marks, and no
  familiar tone. Polite does not mean wordy.
- **Short.** A button is one to three words. A label is a noun phrase. A help text is one sentence, two at most. Leave
  room for languages that run longer: Vietnamese often takes a third more space than English.

## Kinds of text

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

## Placeholders and numbers

- Keep every placeholder, such as `{count}` or `{entity}`, in every locale, and move it to where the language puts it.
- A count reads naturally: English needs "1 field" and "3 fields"; a language without plural forms writes one text.
- Dates, times and numbers come from the locale's formats, never from words in a text.

## The glossary

[`ui-glossary.json`](ui-glossary.json) lists each concept of [concepts.md](../domain/concepts.md) that a screen names,
and the verbs of the common actions, with the term of each locale. A term has a `status`:

- `approved`: the term to use;
- `proposed`: suggested, waiting for the owner;
- `decide`: the owner chooses among the `options` before any text changes.

`avoid` lists words that must not name the concept in that locale, such as a second translation or the English word.
`keep` lists the names no locale translates.

## English

- Plain international English, with British spelling where it differs: "behaviour", "licence" for the noun.
- Address the person as "you" only when a sentence needs a subject; most texts need none.
- An action is a verb: "Add field", not "Field addition".

## Vietnamese

- **Neutral, without a pronoun.** Most texts need no subject: "Nhập tên.", "Không thể lưu bản ghi." Use "bạn" only when a
  sentence cannot do without one. Never "quý khách", "mình", or a familiar tone.
- **Verb first for actions.** "Thêm trường", "Thêm vai trò…", not "Trường mới", "Vai trò mới…".
- **Validation messages as plain imperatives.** "Nhập tên.", "Chọn loại." Not "Vui lòng…" or "Hãy…", which only add
  length.
- **Not the English sentence's shape.**
  - No "của nó" for "its": name the thing, or leave it out. "Hiện dữ liệu" or "Hiện dữ liệu của nguồn", not "Hiện dữ
    liệu của nó".
  - No passive built on "được" or "bị" where English is passive: "Đã xóa vai trò", not "Vai trò đã được xóa".
  - No "thực hiện việc", "tiến hành", "thao tác" wrapped around a verb: "Xóa", not "Thực hiện việc xóa".
  - No literal noun piles from English: "Độ rộng (trên 12 cột)", not "Độ rộng theo phần mười hai của hàng".
- **One spelling.** The modern placement of tone marks: "xóa", "hủy", "hòa". The glossary's verbs follow it.
- **English terms the glossary keeps** stay in English and are not mixed with a translation of the same concept in one
  screen.

## Checks _(planned)_

A check in `scripts/lint.sh` reads the texts and the glossary and fails when:

- a key is missing in a locale, or a placeholder of the English text is missing or added in another locale;
- a text uses a word the glossary's `avoid` lists for its locale;
- a text in another locale is the same as the English one, unless the glossary's `keep` allows it.

## Changing a text

1. Read the key's place on the screen and its purpose, then write every locale of the change from that purpose.
2. A concept with no term yet, or a term to change, is added to the glossary as `proposed` in the same change, for the
   owner to approve.
3. A change that rewrites many texts of one locale goes area by area, with the owner reviewing the first area before
   the rest follow.
