// Proposed (design review 2026-10-30-studio): each empty field shows its own message under it, and the form is sent
// only when both are filled. Typing in a field clears its message. Without script, Keycloak's own refusal still shows.
;(function () {
  var form = document.getElementById('company-sign-in')
  if (!form) return
  var fields = ['username', 'password'].map(function (name) {
    return { input: document.getElementById(name), error: document.getElementById(name + '-error') }
  })

  function show(field, empty) {
    field.error.hidden = !empty
    if (empty) {
      field.input.setAttribute('aria-invalid', 'true')
      field.input.setAttribute('aria-describedby', field.error.id)
    } else {
      field.input.removeAttribute('aria-invalid')
      field.input.removeAttribute('aria-describedby')
    }
  }

  fields.forEach(function (field) {
    field.input.addEventListener('input', function () {
      if (field.input.value !== '') show(field, false)
    })
  })

  form.addEventListener('submit', function (event) {
    var firstEmpty = null
    fields.forEach(function (field) {
      var empty = field.input.value.trim() === ''
      show(field, empty)
      if (empty && !firstEmpty) firstEmpty = field
    })
    if (firstEmpty) {
      event.preventDefault()
      firstEmpty.input.focus()
      return
    }
    document.getElementById('kc-login').disabled = true
  })
})()
