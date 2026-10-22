import { expect, test } from '@playwright/test'
import { pickOption } from '../select'

// The page has no server data. Its option click does nothing, so the pick never lands.
test('pickOption fails at once, naming the select and the option, when the pick did not land', async ({ page }) => {
  test.setTimeout(10_000)
  await page.setContent(`
    <input readonly role="combobox" aria-label="Colour" aria-controls="list" aria-expanded="false">
    <div id="list" role="listbox" hidden>
      <div role="option" aria-selected="false">red</div>
      <div role="option" aria-selected="false">blue</div>
    </div>
    <script>
      const input = document.querySelector('input'), list = document.getElementById('list')
      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { list.hidden = false; input.setAttribute('aria-expanded', 'true') }
        if (e.key === 'Escape') { list.hidden = true; input.setAttribute('aria-expanded', 'false') }
      })
    </script>
  `)
  await expect(pickOption(page.getByRole('combobox', { name: 'Colour', exact: true }), 'red')).rejects.toThrow(
    /Picking 'red' in the select Colour/,
  )
})
