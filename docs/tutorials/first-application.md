# First application

This tutorial builds a small application, ExpenseClaims, in Studio. An employee drafts an expense and
submits it, and a department manager approves it. You build it with Studio only: no file is edited by
hand. Then you check it, publish it, and use it on its site.

The steps below are the ones the journey test `first-application-tutorial.spec.ts` runs on the journey
tenant, so they stay true to what Studio does. Each step is one test with the same heading.

## Before you start

Start Axis with `scripts/dev.sh`, as [hot reload with scripts/dev.sh](../development.md#hot-reload-with-scriptsdevsh)
describes. Studio then runs at http://localhost:5173/studio.

Sign in as a user who holds the **author** role, which builds the application. Publishing also needs the
**publisher** role, which is set by `Studio:PublisherRole` and is not a role of the application. A
development user with both roles can build and publish. To sign in as a person with another role, use a
development user that holds that role. The [development users](../reference/security.md#development-users)
table lists them, and [the axis realm](../development.md#the-axis-realm) gives the password and the
sign-in code.

The application has two roles, and each one does a different job:

- **employee** drafts an expense and submits it.
- **department-manager** reviews the submitted expense and approves it.

A role is a named group of people that the application's policies refer to. Words like entity, process
and data source are defined in [Concepts](../domain/concepts.md). The short version used here:

- An **application** is a folder of resources that Studio saves, checks and publishes as a release.
- A **resource** is one file of the application, such as an entity, a form, a process or a page.
- An **entity** is a kind of record with fields, such as an expense with an amount.
- A **process** is a series of steps that a record goes through, such as a submission and a review.

## 1. Create the application

Open Studio and choose **New application…**. Give it the name `ExpenseClaims` and choose **Create**.
Studio starts from the blank application template, which has one site, one home page with the task
inbox, and the English texts of that site. The [modules and templates](../reference/modules-and-templates.md)
page says what a template holds.

## 2. Add the texts

Every label stores a text key, so the words on screen can be changed without touching the resources
that use them. When you type a label's text into the label, Studio makes the key for it. A field and a
task pick their text from a list instead, so their keys are added here first. Open the **Text** kind
and choose the English file. In the text editor, add each key with its English text:

- `expense.name`, `expense.amount` and `expense.status`, the labels of the fields
- `task.review`, `task.approve` and `task.reject`, the label and outcomes of the review task

Type the key in **New key**, its English text in **en**, and choose **Add key**. The **New key** box
clears, ready for the next one.

## 3. Add the roles

Create a role named `employee` and a role named `department-manager`. Each role has a label. Type its
English text into the label: **Employee** for the first role and **Department manager** for the second.
Choose **Save** after each one.

A role only gives a name to a group of people. What each role may do is set later, in the policy matrix.

## 4. Build the Expense entity

Create an entity named `Expense` and give it the label **Expense**. Then set its fields:

1. The first field is `name`, which Studio adds with the entity. Tick **Required**, and give it the label
   `expense.name`. The display field must be required, or Validate reports an error.
2. Add a field `amount` of type `decimal`, with the label `expense.amount`.
3. Add a field `status` of type `enum`. Its values are `draft`, `submitted`, `approved` and `rejected`.
   Set its default to `'draft'`, so a new expense starts as a draft. Give it the label `expense.status`.

Set the display field to `name`, so a row in a list shows the expense's name. Choose **Save**. Studio asks
what to do with the empty values of `name`, because it is now required. The expense has no records yet,
so choose **Leave nulls**. The page shows **Saved**, and the entity has no error.

An enum is a field that takes one of a fixed list of values. The default is an expression, and the
quotes make it a text value.

## 5. Lay out the two forms

A form is the screen that shows or edits the fields of one entity. Create two forms for `Expense`.

- **ExpenseEdit** is the form the employee fills in. Its section is named `details` and has the
  section title **Details**. It shows `name` and `amount`, and both can be changed.
- **ExpenseReview** is the form the manager reads. Its section is named `details` and has the section
  title **Review**. It shows `name`, `amount` and `status`, and all three are read-only.

Choose **Save** on each form. The read-only fields matter later: the manager reviews an expense and
does not change it.

## 6. Build the approval process

Create a process named `ExpenseApproval` for the entity `Expense`. The process runs the following
steps, in this order:

| Step           | Type      | What it does                                                                                    |
| -------------- | --------- | ----------------------------------------------------------------------------------------------- |
| `submit`       | Operation | Sets `status` to `'submitted'`, then goes to `review`                                           |
| `review`       | Task      | Asks a `department-manager` to review the expense on `ExpenseReview`, due in three days (`P3D`) |
| `markApproved` | Operation | Sets `status` to `'approved'`, then goes to `done`                                              |
| `markRejected` | Operation | Sets `status` to `'rejected'`, then goes to `done`                                              |
| `done`         | End       | Ends the process                                                                                |

The task `review` has two outcomes, **Approve** (`task.approve`, to `markApproved`) and **Reject**
(`task.reject`, to `markRejected`). Its label is `task.review`, which shows as the task's name in the
inbox.

The process starts at `submit`. Its start condition is `status == 'draft'`, and when it is false the
start fails with the message **Only a draft expense can be submitted.** So an expense that is already
submitted cannot be submitted twice.

Every step name exists before the transitions are chosen, because each transition picks from the names
that exist. Choose **Save** when the process is complete.

## 7. Build the pages and the site

This step has three parts.

First, a data source named `ExpenseList` for the entity `Expense`, with the fields `name`, `amount` and
`status`. A table shows the records of a data source, so the list page reads from it.

Second, two pages:

- **ExpenseForm**, titled **Expense**. It holds the form widget `ExpenseEdit` and one action,
  **Submit**, which starts `ExpenseApproval`.
- **Expenses**, titled **Expenses**. It holds a table of `ExpenseList`, with one toolbar action **New**
  and one row action **Open**. Both open `ExpenseForm`.
  **Open** passes the row's `id`, so the form shows that expense.

Third, the site. Add a navigation entry for `Expenses` after the entry for the home page, with the label
**Expenses**. Choose **Save**.

The table shows what the data source holds, and the form shows what the entity holds. Both are needed:
the form saves a record, and the list reads it.

## 8. Grant access in the policy matrix

Open the **Policy matrix** and grant each role what it needs. A grant lists the actions a role may take
on a target, and it can carry a filter that limits which records it applies to.

| Role                 | Target                    | Actions            | Filter              |
| -------------------- | ------------------------- | ------------------ | ------------------- |
| `employee`           | `Expense` entity          | `read`             |                     |
| `employee`           | `Expense` entity          | `create`, `update` | `status == 'draft'` |
| `employee`           | `ExpenseList` data source | `read`             |                     |
| `employee`           | `ExpenseApproval` process | `start`            |                     |
| `department-manager` | `Expense` entity          | `read`             |                     |
| `department-manager` | `ExpenseList` data source | `read`             |                     |

The employee can create and change an expense only while its status is `draft`, so a submitted expense
can still be read but no longer changed by the employee. The manager can read expenses and their list,
and has no grant to create or change one. Roles and policies are described in
[Roles and policies](../reference/roles-and-policies.md).

## 9. Check the application

Choose **Validate**. Studio checks every resource together and reports the result in the request status.
When the check succeeds, the application has no problem, and the problems button does not show. Studio
does not publish anything at this step.

## 10. Publish the application

Choose **Publish…**, then confirm. Studio freezes the application as release 1 and makes it the active
release. The release history shows one row, marked as active.

A release is an immutable copy of the application. Changing the application later does not change release
1, and a new release is published only when you publish again.

## 11. Submit an expense as an employee

Sign in as a user with the **employee** role and open the **Expenses** entry of the site. Choose **New**
and fill in:

- **Name**, such as a short title for the expense
- **Amount**, such as `42.50`

Choose **Save**. The page returns to the list, and the new expense is a draft. Open it from its row, then
choose **Submit**. The page reports that the process has started.

## 12. Approve the expense as a department manager

Sign in as a user with the **department-manager** role and open the site's home page. The task
**Review expense** lists the submitted expense. Open it, and choose **Approve**. The task closes and the
process moves to `markApproved`, which sets the status to `approved`.

On the **Expenses** page, the manager sees the expense and has no **New** button, because the manager's
grants allow reading only. Then sign in as the employee again: the expense is in the list with the status
`approved`.

## Where to go next

- [Concepts](../domain/concepts.md) defines each kind of resource used here.
- [Entity designer](../reference/entity-designer.md) and [Process designer](../reference/process-designer.md)
  describe every setting the steps touched.
- [Processes](../reference/processes.md) describes the steps, their transitions and the start condition.
