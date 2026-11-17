# Content widget

The content widget shows fixed text, an image, a link or a note on a page, with no records behind it. This page says how
a page names one, what each kind shows and what the compiler checks. The rest of the frontend is in
[frontend](frontend.md).

## Content widget

_(planned for M42)_

A content widget shows what the application writes, not what records hold. A page names one like this:

```json
{ "type": "content", "name": "welcome", "kind": "text", "text": { "textKey": "pages.home.welcome" } }
```

- **Kinds.** The `kind` is one of four:
  - `text` shows plain text, and keeps its line breaks. Its `text` is a text key.
  - `note` shows a text set apart as a note. Its `text` is a text key.
  - `image` shows an image. `image` names a PNG, JPEG or WebP file in the application's `images` folder, such as
    `images/welcome.png`, and `alt` is a text key that describes it. An SVG file is refused. The release keeps the
    file, and the server sends it from its own origin, so the page needs no image host of its own, see
    [security headers](headers-and-rate-limits.md#security-headers).
  - `link` shows a `label` text key as a link. Its target is either an absolute `https` `href`, which opens in a new
    tab, or a `navigate` action to a page of the application, see [navigate](table-widget.md#navigate).
- **In each language.** Every text is a text key, so each locale's text resource gives its own words. The keys are
  checked as every text key is, see [text resolution](frontend.md#text-resolution).
- **Access.** A content widget shows to anyone who may open the page. It owns no part of the address and reads no
  records. It does not count as a readable widget: alone, it does not make a page readable, see
  [policies](frontend.md#policies).
- **Compile.** The compiler refuses a property that the kind does not take, a property that the kind needs and lacks,
  an image file that is not in the `images` folder, and an `href` that is not an absolute `https` address. A
  `navigate` action of a link is checked as every `navigate` action is, see
  [navigate and row actions](configuration.md#navigate-and-row-actions).
- **Look.** The look is left to design reviews, as [design changes](../delivery.md#design-changes) says.
