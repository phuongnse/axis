# When something fails

| Symptom                                           | What to do                                                                                   |
| ------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| The artifact link does not open                   | Ask the owner. The artifact is private to them.                                              |
| `scripts/design-system.sh` fails                  | It needs the Playwright browser from `scripts/setup.sh`. Run that, then try again.           |
| A change note names a closed issue never merged   | Ask the owner whether to drop the change or to open a new issue. Do not drop it by yourself. |
| An issue named in a note cannot be found          | Ask the owner. Keep the change until they answer.                                            |
| Save or Next does nothing in the artifact         | The frame is sandboxed without `allow-forms`. Check the bundle still fires the submit event. |
| A dash shows as garbled characters, such as "â€”"  | The page lacks `<meta charset="utf-8">`. Add it and check the card again.                  |
| A card is cut off or ends in an empty band        | Measure its content height again at 992 px and set the marker's height to it.               |
| A change note is refused                          | It is over 280 characters. Shorten it and keep the review's name and branch.                 |
| A link to the prototype gives a 404              | It points at a branch, which was deleted. Link the review's `design-review/` tag instead.    |
