/**
 * Axis Screen Wireframes — generate-screens.mjs
 * Run: node docs/wireframes/generate-screens.mjs
 *
 * All visual components are sourced from generate-template.mjs builders.
 * Use component(buildXxx, x, y) to place template sections into screens.
 *
 * Screens generated:
 *   _shared/app-shell
 *   E02: settings-users, settings-roles, settings-security, accept-invitation
 *   E03: data-models, data-classes, records
 *   E04: workflows, workflow-editor
 *   E05: forms, form-editor, form-submission
 *   E06: executions, execution-detail
 */

import { mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

import {
  C, SB, HDR, CX, CY,
  rect, ellipse, text, hline, vline, arrow,
  btn, inputField, selectField, badge, searchBar, pageHeader,
  appShell, component, translate, writeExcalidraw,
} from './components.mjs';

import {
  buildWorkflowCanvas,
  buildBuilderLayout,
  buildExecutionTimeline,
  buildModal,
  buildSideSheet,
  buildTable,
} from './generate-template.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dir = dirname(__filename);

// ─── Screen constants ─────────────────────────────────────────────────────────

const W   = 1100;  // screen width  (SB=230 + content=850 + margin=20)
const H   = 700;   // screen height
const PAD = 20;    // content area padding

// Content area origin with padding
const cx = CX + PAD;   // 250  — first content element x
const cy = CY + PAD;   // 80   — first content element y
const cw = W - CX - PAD * 2;  // 830  — usable content width

// Standard app nav labels
const NAV = ['Data Models', 'Workflows', 'Forms', 'Executions', 'Settings'];

// ─── Write helper ─────────────────────────────────────────────────────────────

function write(relativePath, elements) {
  const full = join(__dir, relativePath);
  mkdirSync(dirname(full), { recursive: true });
  writeExcalidraw(full, elements);
  console.log(`✓  ${relativePath}  (${elements.length} elements)`);
}

// ─── _shared ─────────────────────────────────────────────────────────────────

function genAppShell() {
  const els = [
    ...appShell('as', W, H, NAV, 0, 'Dashboard'),
    // Content area placeholder
    rect('as_content', cx, cy, cw, H - CY - PAD * 2, C.gray300, C.white, 1, true),
    text('as_ph', cx + 20, cy + 20, 200, 24, 'Content area', 14, C.gray500),
  ];
  write('_shared/app-shell.excalidraw', els);
}

// ─── E02 Identity & Access ────────────────────────────────────────────────────

function genSettingsUsers() {
  const navIdx = 4; // Settings
  const tblY = cy + 64;
  const tblH = H - tblY - PAD;
  const cols  = [280, 120, 140, 100, 130, 60];
  const xC    = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW  = cols.reduce((s, w) => s + w, 0);

  const headers = ['Name / Email', 'Role', 'Status', 'Last Login', 'Invited', 'Actions'];
  const rows = [
    { name: 'Alex Brown',  email: 'alex@acme.com',  role: 'Admin',  status: 'active',  login: '2 min ago',   inv: 'Jan 1' },
    { name: 'Jane Smith',  email: 'jane@acme.com',  role: 'Editor', status: 'active',  login: '1 hr ago',    inv: 'Jan 5' },
    { name: 'Mark J.',     email: 'mark@acme.com',  role: 'Viewer', status: 'pending', login: '—',           inv: 'Today' },
    { name: 'Sarah Lee',   email: 'sarah@acme.com', role: 'Editor', status: 'active',  login: 'Yesterday',   inv: 'Jan 8' },
  ];

  const els = [
    ...appShell('su', W, H, NAV, navIdx, 'Settings — Users'),

    // Toolbar
    ...searchBar('su_s', cx, cy, 280),
    ...btn('su_inv', cx + cw - 152, cy + 2, '+ Invite User'),

    // Table
    rect('su_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('su_hdr', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('su_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`su_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`su_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, email, role, status, login, inv }, i) => {
      const y = tblY + 44 + i * 50;
      const statusVariant = status === 'active' ? 'success' : 'warning';
      return [
        rect(`su_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`su_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`su_nm_${i}`, xC[0] + 12, y + 8, cols[0] - 16, 18, name, 13, C.gray900),
        text(`su_em_${i}`, xC[0] + 12, y + 28, cols[0] - 16, 14, email, 11, C.gray500),
        text(`su_rl_${i}`, xC[1] + 12, y + 15, cols[1] - 16, 20, role, 13, C.gray700),
        ...badge(`su_st_${i}`, xC[2] + 12, y + 11, status === 'active' ? 'Active' : 'Pending', statusVariant),
        text(`su_lg_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, login, 12, C.gray700),
        text(`su_iv_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, inv, 12, C.gray700),
        ...btn(`su_edit_${i}`, xC[5] + 6, y + 7, '…', 'ghost'),
      ];
    }),
    text('su_foot', cx + 12, tblY + tblH + 4, 300, 18, 'Showing 1–4 of 12 users', 12, C.gray500),
  ];
  write('E02-identity-access/settings-users.excalidraw', els);
}

function genSettingsRoles() {
  const navIdx = 4;
  const els = [
    ...appShell('sr', W, H, NAV, navIdx, 'Settings — Roles'),

    // Roles list (left panel)
    rect('sr_panel', cx, cy, 240, H - CY - PAD * 2, C.gray300, C.white, 1, false),
    text('sr_panel_t', cx + 16, cy + 14, 200, 20, 'Roles', 14, C.gray900),
    hline('sr_panel_div', cx, cy + 44, 240, C.gray300),
    ...['Admin', 'Editor', 'Viewer', 'Guest'].flatMap((role, i) => {
      const y = cy + 52 + i * 44;
      const active = i === 0;
      return [
        rect(`sr_ri_${i}`, cx, y, 240, 40, active ? C.infoBorder : 'transparent', active ? C.infoBg : 'transparent', 1, false),
        ...(active ? [rect(`sr_racc_${i}`, cx, y, 3, 40, C.primary, C.primary, 1, false)] : []),
        text(`sr_rl_${i}`, cx + 16, y + 11, 160, 18, role, 13, active ? C.primary : C.gray700),
        text(`sr_rc_${i}`, cx + 188, y + 11, 36, 18, ['4', '2', '1', '0'][i] + ' users', 11, C.gray500, 'right'),
      ];
    }),
    ...btn('sr_add_role', cx + 16, cy + H - CY - PAD * 2 - 48, '+ New Role', 'secondary'),

    // Permissions panel (right)
    rect('sr_perm', cx + 256, cy, cw - 256, H - CY - PAD * 2, C.gray300, C.white, 1, false),
    text('sr_perm_t', cx + 272, cy + 14, 300, 20, 'Admin — Permissions', 14, C.gray900),
    hline('sr_perm_div', cx + 256, cy + 44, cw - 256, C.gray300),
    // Permission rows
    ...['execution:read', 'execution:write', 'execution:retry', 'workflow:read', 'workflow:write', 'data:read', 'data:write', 'settings:read', 'settings:write'].flatMap((perm, i) => {
      const y = cy + 60 + i * 40;
      const enabled = i < 7;
      return [
        text(`sr_pm_${i}`, cx + 272, y + 11, 300, 18, perm, 12, C.gray700),
        rect(`sr_chk_${i}`, cx + cw - 60, y + 9, 20, 20, enabled ? C.primary : C.gray300, enabled ? C.primary : C.white, 1, false),
        ...(enabled ? [text(`sr_chkt_${i}`, cx + cw - 60, y + 10, 20, 16, '✓', 11, C.white, 'center')] : []),
        hline(`sr_pdiv_${i}`, cx + 256, y + 40, cw - 256, C.gray300),
      ];
    }),
    ...btn('sr_save', cx + cw - 100, cy + H - CY - PAD * 2 - 48, 'Save'),
  ];
  write('E02-identity-access/settings-roles.excalidraw', els);
}

function genSettingsSecurity() {
  const navIdx = 4;
  const els = [
    ...appShell('ss', W, H, NAV, navIdx, 'Settings — Security'),

    text('ss_s1', cx, cy, 300, 22, 'Password Policy', 16, C.gray900),
    hline('ss_s1div', cx, cy + 26, cw, C.gray300),
    text('ss_min_lbl', cx, cy + 44, 240, 16, 'Minimum password length', 13, C.gray700),
    ...inputField('ss_min', cx + 320, cy + 36, 100, '12'),
    text('ss_comp_lbl', cx, cy + 104, 240, 16, 'Require uppercase letters', 13, C.gray700),
    rect('ss_comp_chk', cx + 320, cy + 102, 20, 20, C.primary, C.primary, 1, false),
    text('ss_comp_chkt', cx + 320, cy + 103, 20, 16, '✓', 11, C.white, 'center'),
    text('ss_exp_lbl', cx, cy + 140, 240, 16, 'Password expiry (days)', 13, C.gray700),
    ...inputField('ss_exp', cx + 320, cy + 132, 100, '90'),

    text('ss_s2', cx, cy + 200, 300, 22, 'Multi-Factor Authentication', 16, C.gray900),
    hline('ss_s2div', cx, cy + 226, cw, C.gray300),
    text('ss_mfa_lbl', cx, cy + 244, 240, 16, 'Require MFA for all users', 13, C.gray700),
    rect('ss_mfa_tog', cx + 320, cy + 242, 44, 24, C.primary, C.primary, 2, true),
    rect('ss_mfa_knob', cx + 342, cy + 244, 20, 20, C.primaryDark, C.white, 1, true),

    text('ss_s3', cx, cy + 300, 300, 22, 'Session Management', 16, C.gray900),
    hline('ss_s3div', cx, cy + 326, cw, C.gray300),
    text('ss_sess_lbl', cx, cy + 344, 240, 16, 'Session timeout (minutes)', 13, C.gray700),
    ...inputField('ss_sess', cx + 320, cy + 336, 100, '60'),

    ...btn('ss_save', cx + cw - 120, cy + 420, 'Save Changes'),
  ];
  write('E02-identity-access/settings-security.excalidraw', els);
}

function genAcceptInvitation() {
  // Standalone (no sidebar) — centered card
  const cardW = 440, cardH = 380;
  const cardX = (W - cardW) / 2, cardY = (H - cardH) / 2;
  const els = [
    rect('ai_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false),
    rect('ai_card', cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true),
    text('ai_logo', cardX + cardW / 2 - 40, cardY + 24, 80, 26, '⬡  Axis', 18, C.primary, 'center'),
    hline('ai_hdiv', cardX, cardY + 60, cardW, C.gray300),
    text('ai_title', cardX + 24, cardY + 76, cardW - 48, 26, 'You have been invited', 18, C.gray900),
    text('ai_sub', cardX + 24, cardY + 108, cardW - 48, 18, 'Join Acme Corp on Axis', 13, C.gray700),
    text('ai_org_lbl', cardX + 24, cardY + 148, 100, 16, 'Organization', 11, C.gray500),
    ...inputField('ai_org', cardX + 24, cardY + 166, cardW - 48, 'Acme Corp'),
    text('ai_pw_lbl', cardX + 24, cardY + 224, 100, 16, 'Choose a password', 11, C.gray500),
    ...inputField('ai_pw', cardX + 24, cardY + 242, cardW - 48, '••••••••'),
    ...btn('ai_accept', cardX + 24, cardY + 308, 'Accept Invitation'),
    hline('ai_fdiv', cardX, cardY + 355, cardW, C.gray300),
    text('ai_signin', cardX + 24, cardY + 363, cardW - 48, 16, 'Already have an account? Sign in', 12, C.primary, 'center'),
  ];
  write('E02-identity-access/accept-invitation.excalidraw', els);
}

// ─── E03 Data Modeling ────────────────────────────────────────────────────────

function genDataModels() {
  const navIdx = 0;
  const els = [
    ...appShell('dm', W, H, NAV, navIdx, 'Data Models'),
    // Toolbar
    ...searchBar('dm_s', cx, cy, 280),
    ...btn('dm_add', cx + cw - 136, cy + 2, '+ New Model'),
    // Model cards grid (3 per row)
    ...[
      { name: 'User Profile',  fields: 8,  records: '1.2k', status: 'active' },
      { name: 'Order',         fields: 12, records: '4.8k', status: 'active' },
      { name: 'Product',       fields: 6,  records: '312',  status: 'active' },
      { name: 'Invoice',       fields: 14, records: '890',  status: 'active' },
      { name: 'Task',          fields: 9,  records: '2.1k', status: 'draft'  },
      { name: 'Company',       fields: 7,  records: '156',  status: 'active' },
    ].flatMap(({ name, fields, records, status }, i) => {
      const col = i % 3, row = Math.floor(i / 3);
      const cardW = 248, cardH = 130;
      const x = cx + col * (cardW + 16);
      const y = cy + 56 + row * (cardH + 16);
      return [
        rect(`dm_card_${i}`, x, y, cardW, cardH, C.gray300, C.white, 1, true),
        rect(`dm_ch_${i}`,   x, y, cardW, 48, C.gray300, C.gray50, 1, false, { roundness: null }),
        text(`dm_cn_${i}`,   x + 14, y + 14, cardW - 28, 20, name, 14, C.gray900),
        ...badge(`dm_st_${i}`, x + cardW - 68, y + 12, status === 'active' ? 'Active' : 'Draft', status === 'active' ? 'success' : 'draft'),
        text(`dm_fi_${i}`,   x + 14, y + 64, 100, 16, `${fields} fields`, 12, C.gray500),
        text(`dm_re_${i}`,   x + 14, y + 84, 100, 16, `${records} records`, 12, C.gray500),
        hline(`dm_ca_${i}`,  x, y + cardH - 32, cardW, C.gray300),
        ...btn(`dm_open_${i}`, x + 14, y + cardH - 26, 'Open', 'secondary'),
      ];
    }),
  ];
  write('E03-data-modeling/data-models.excalidraw', els);
}

function genDataClasses() {
  const navIdx = 0;
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [220, 100, 160, 100, 110, 140];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Class Name', 'Module', 'Description', 'Fields', 'Status', 'Actions'];
  const rows = [
    { name: 'UserProfile',   mod: 'Identity',      desc: 'User account data',   fields: 8,  status: 'active' },
    { name: 'OrderRecord',   mod: 'Commerce',      desc: 'Order transactions',   fields: 12, status: 'active' },
    { name: 'ProductItem',   mod: 'Inventory',     desc: 'Catalog products',     fields: 6,  status: 'draft'  },
    { name: 'InvoiceEntry',  mod: 'Finance',       desc: 'Billing records',      fields: 14, status: 'active' },
  ];
  const els = [
    ...appShell('dc', W, H, NAV, navIdx, 'Data Classes'),
    ...searchBar('dc_s', cx, cy, 280),
    ...btn('dc_add', cx + cw - 148, cy + 2, '+ New Class'),
    rect('dc_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('dc_hdr', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('dc_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`dc_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`dc_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, mod, desc, fields, status }, i) => {
      const y = tblY + 44 + i * 50;
      return [
        rect(`dc_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`dc_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`dc_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name, 13, C.gray900),
        text(`dc_md_${i}`, xC[1] + 12, y + 15, cols[1] - 16, 20, mod, 12, C.gray700),
        text(`dc_ds_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, desc, 12, C.gray700),
        text(`dc_fi_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, `${fields}`, 13, C.gray700),
        ...badge(`dc_st_${i}`, xC[4] + 12, y + 11, status === 'active' ? 'Active' : 'Draft', status === 'active' ? 'success' : 'draft'),
        ...btn(`dc_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
  ];
  write('E03-data-modeling/data-classes.excalidraw', els);
}

function genRecords() {
  const navIdx = 0;
  const tblY = cy + 64;
  const tblH = H - tblY - PAD;
  const cols = [200, 120, 140, 130, 100, 100];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Name', 'Status', 'Owner', 'Created', 'Updated', 'Actions'];
  const rows = [
    { name: 'Acme Corporation', status: 'active',  owner: 'Alex Brown', created: 'Jan 12', updated: 'Today' },
    { name: 'Globex Inc',       status: 'pending', owner: 'Jane Smith',  created: 'Jan 10', updated: '1h ago' },
    { name: 'Initech Ltd',      status: 'active',  owner: 'Mark J.',     created: 'Jan 8',  updated: 'Yesterday' },
    { name: 'Umbrella Corp',    status: 'draft',   owner: 'Sarah Lee',   created: 'Jan 5',  updated: '3d ago' },
  ];
  const els = [
    ...appShell('rec', W, H, NAV, navIdx, 'Records — User Profile'),
    // Breadcrumb
    text('rec_bc', cx, cy, 300, 18, 'Data Models  ›  User Profile', 12, C.gray500),
    // Toolbar
    ...searchBar('rec_s', cx, cy + 24, 280),
    ...selectField('rec_flt', cx + 296, cy + 24, 140, 'Filter by status'),
    ...btn('rec_add', cx + cw - 128, cy + 26, '+ New Record'),
    // Table
    rect('rec_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('rec_hdr', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('rec_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`rec_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`rec_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, status, owner, created, updated }, i) => {
      const y = tblY + 44 + i * 50;
      const sv = status === 'active' ? 'success' : status === 'pending' ? 'warning' : 'draft';
      return [
        rect(`rec_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`rec_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`rec_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name, 13, C.gray900),
        ...badge(`rec_st_${i}`, xC[1] + 12, y + 11, status.charAt(0).toUpperCase() + status.slice(1), sv),
        text(`rec_ow_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, owner, 12, C.gray700),
        text(`rec_cr_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, created, 12, C.gray700),
        text(`rec_up_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, updated, 12, C.gray700),
        ...btn(`rec_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
    text('rec_foot', cx + 12, tblY + tblH + 4, 300, 18, 'Showing 1–4 of 142 records', 12, C.gray500),
  ];
  write('E03-data-modeling/records.excalidraw', els);
}

// ─── E04 Workflow Builder ─────────────────────────────────────────────────────

function genWorkflows() {
  const navIdx = 1;
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [220, 90, 120, 120, 120, 100];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Workflow Name', 'Status', 'Trigger', 'Last Run', 'Runs (30d)', 'Actions'];
  const rows = [
    { name: 'Order Processing',  status: 'active', trigger: 'Form Submit', run: '2 min ago',  runs: 142 },
    { name: 'User Onboarding',   status: 'active', trigger: 'Schedule',    run: '1 hr ago',   runs: 28  },
    { name: 'Invoice Generator', status: 'draft',  trigger: 'Manual',      run: '—',          runs: 0   },
    { name: 'Email Drip',        status: 'active', trigger: 'Schedule',    run: 'Yesterday',  runs: 56  },
    { name: 'Approval Flow',     status: 'paused', trigger: 'Form Submit', run: '3 days ago', runs: 14  },
  ];
  const els = [
    ...appShell('wf', W, H, NAV, navIdx, 'Workflows'),
    ...searchBar('wf_s', cx, cy, 200),
    ...selectField('wf_flt', cx + 216, cy, 160, 'All statuses'),
    ...btn('wf_add', cx + cw - 160, cy + 2, '+ New Workflow'),
    rect('wf_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('wf_hdr', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('wf_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`wf_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`wf_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, status, trigger, run, runs }, i) => {
      const y = tblY + 44 + i * 50;
      const sv = status === 'active' ? 'success' : status === 'paused' ? 'warning' : 'draft';
      return [
        rect(`wf_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`wf_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`wf_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name, 13, C.gray900),
        ...badge(`wf_st_${i}`, xC[1] + 12, y + 11, status.charAt(0).toUpperCase() + status.slice(1), sv),
        text(`wf_tr_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, trigger, 12, C.gray700),
        text(`wf_ru_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, run, 12, C.gray700),
        text(`wf_rn_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, `${runs}`, 12, C.gray700),
        ...btn(`wf_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
  ];
  write('E04-workflow-builder/workflows.excalidraw', els);
}

function genWorkflowEditor() {
  const navIdx = 1;
  // The workflow canvas component from the template — placed in the content area
  const canvasEls = component(buildWorkflowCanvas, cx, cy);
  const els = [
    ...appShell('we', W, H, NAV, navIdx, 'Workflow Editor — Order Processing'),
    ...canvasEls,
  ];
  write('E04-workflow-builder/workflow-editor.excalidraw', els);
}

// ─── E05 Form Builder ─────────────────────────────────────────────────────────

function genForms() {
  const navIdx = 2;
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [220, 80, 120, 140, 120, 90];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Form Name', 'Status', 'Workflow', 'Last Submission', 'Submissions', 'Actions'];
  const rows = [
    { name: 'Contact Form',      status: 'active', wf: 'Order Processing', sub: '5 min ago', total: 342 },
    { name: 'Onboarding Survey', status: 'active', wf: 'User Onboarding',  sub: '1 hr ago',  total: 128 },
    { name: 'Feedback Form',     status: 'draft',  wf: '—',                sub: '—',         total: 0   },
    { name: 'Support Request',   status: 'active', wf: 'Approval Flow',    sub: 'Yesterday', total: 67  },
  ];
  const els = [
    ...appShell('frm', W, H, NAV, navIdx, 'Forms'),
    ...searchBar('frm_s', cx, cy, 240),
    ...btn('frm_add', cx + cw - 140, cy + 2, '+ New Form'),
    rect('frm_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('frm_hdr', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('frm_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`frm_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`frm_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, status, wf, sub, total }, i) => {
      const y = tblY + 44 + i * 50;
      return [
        rect(`frm_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`frm_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`frm_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name, 13, C.gray900),
        ...badge(`frm_st_${i}`, xC[1] + 12, y + 11, status === 'active' ? 'Active' : 'Draft', status === 'active' ? 'success' : 'draft'),
        text(`frm_wf_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, wf, 12, C.gray700),
        text(`frm_sb_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, sub, 12, C.gray700),
        text(`frm_to_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, `${total}`, 12, C.gray700),
        ...btn(`frm_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
  ];
  write('E05-form-builder/forms.excalidraw', els);
}

function genFormEditor() {
  const navIdx = 2;
  // Use the builder layout component from the template
  const builderEls = component(buildBuilderLayout, cx, cy);
  const els = [
    ...appShell('fe', W, H, NAV, navIdx, 'Form Editor — Contact Form'),
    ...builderEls,
  ];
  write('E05-form-builder/form-editor.excalidraw', els);
}

function genFormSubmission() {
  const navIdx = 2;
  // Two-column: submission detail (left) + side sheet pattern (right)
  const sideEls = component(buildSideSheet, cx + 520, cy);
  const els = [
    ...appShell('fs', W, H, NAV, navIdx, 'Form Submission — Contact Form'),
    // Submission list (left panel)
    text('fs_lbl', cx, cy, 200, 20, 'Submissions', 16, C.gray900),
    rect('fs_list', cx, cy + 30, 500, H - CY - PAD * 2 - 30, C.gray300, C.white, 1, false),
    rect('fs_list_hdr', cx, cy + 30, 500, 44, 'transparent', C.gray100, 0, false),
    hline('fs_ldiv', cx, cy + 74, 500, C.gray300),
    ...['Jane Smith — Jan 12, 14:32', 'Mark J. — Jan 12, 13:15', 'Sarah Lee — Jan 11, 09:40', 'Alex B. — Jan 10, 16:22'].flatMap((entry, i) => {
      const y = cy + 82 + i * 52;
      const active = i === 0;
      return [
        rect(`fs_li_${i}`, cx, y, 500, 50, active ? C.infoBorder : 'transparent', active ? C.infoBg : 'transparent', 1, false),
        text(`fs_li_t_${i}`, cx + 16, y + 15, 430, 20, entry, 13, active ? C.primary : C.gray700),
        text(`fs_li_s_${i}`, cx + 430, y + 15, 56, 20, active ? '→' : '', 13, C.primary, 'right'),
        ...(i < 3 ? [hline(`fs_li_d_${i}`, cx, y + 50, 500, C.gray300)] : []),
      ];
    }),
    // Side sheet from template
    ...sideEls,
  ];
  write('E05-form-builder/form-submission.excalidraw', els);
}

// ─── E06 Workflow Engine ──────────────────────────────────────────────────────

function genExecutions() {
  const navIdx = 3;
  const tblY = cy + 64;
  const tblH = H - tblY - PAD;
  const cols = [130, 90, 120, 120, 120, 130, 60];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Execution ID', 'Status', 'Workflow', 'Trigger', 'Started', 'Duration', 'Actions'];
  const rows = [
    { id: 'exec-1042', status: 'success', wf: 'Order Processing',  trigger: 'Form Submit', started: '2 min ago',   dur: '14 ms' },
    { id: 'exec-1041', status: 'running', wf: 'User Onboarding',   trigger: 'Schedule',    started: '5 min ago',   dur: '…'     },
    { id: 'exec-1040', status: 'failed',  wf: 'Invoice Generator', trigger: 'Manual',      started: '1 hr ago',    dur: '203 ms'},
    { id: 'exec-1039', status: 'success', wf: 'Approval Flow',     trigger: 'Form Submit', started: 'Yesterday',   dur: '8 ms'  },
    { id: 'exec-1038', status: 'success', wf: 'Email Drip',        trigger: 'Schedule',    started: 'Yesterday',   dur: '22 ms' },
  ];
  const statusMap = {
    success: ['success', 'Completed'],
    running: ['info',    'Running'],
    failed:  ['danger',  'Failed'],
  };
  const els = [
    ...appShell('ex', W, H, NAV, navIdx, 'Executions'),
    ...searchBar('ex_s', cx, cy, 220),
    ...selectField('ex_st', cx + 236, cy, 160, 'All statuses'),
    ...selectField('ex_wf', cx + 412, cy, 180, 'All workflows'),
    ...btn('ex_exp', cx + cw - 104, cy + 2, 'Export CSV', 'ghost'),
    rect('ex_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('ex_hdr', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('ex_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`ex_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`ex_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ id, status, wf, trigger, started, dur }, i) => {
      const y = tblY + 44 + i * 50;
      const [sv, sl] = statusMap[status];
      return [
        rect(`ex_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`ex_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`ex_id_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, id, 12, C.primary),
        ...badge(`ex_st_${i}`, xC[1] + 8, y + 11, sl, sv),
        text(`ex_wf_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, wf, 12, C.gray700),
        text(`ex_tr_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, trigger, 12, C.gray700),
        text(`ex_st2_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, started, 12, C.gray700),
        text(`ex_du_${i}`, xC[5] + 12, y + 15, cols[5] - 16, 20, dur, 12, C.gray700),
        ...btn(`ex_vw_${i}`, xC[6] + 8, y + 11, '→', 'ghost'),
      ];
    }),
    text('ex_foot', cx + 12, tblY + tblH + 4, 300, 18, 'Showing 1–5 of 1,247 executions', 12, C.gray500),
  ];
  write('E06-workflow-engine/executions.excalidraw', els);
}

function genExecutionDetail() {
  const navIdx = 3;
  // The execution timeline component from the template — placed in the content area
  const timelineEls = component(buildExecutionTimeline, cx, cy + 40);
  const els = [
    ...appShell('ed', W, H, NAV, navIdx, 'Execution Detail — exec-1042'),
    // Breadcrumb
    text('ed_bc', cx, cy, 400, 18, 'Executions  ›  exec-1042', 12, C.gray500),
    // Execution meta header
    text('ed_id', cx, cy + 22, 300, 22, 'Run #1042', 18, C.gray900),
    ...badge('ed_st', cx + 164, cy + 26, 'Completed', 'success'),
    text('ed_meta', cx + 280, cy + 30, 300, 16, 'Order Processing · 14 ms · 2 min ago', 12, C.gray500),
    ...btn('ed_retry', cx + cw - 176, cy + 20, 'Retry', 'ghost'),
    ...btn('ed_retry_ctx', cx + cw - 320, cy + 20, 'Retry with context…', 'ghost'),
    // Execution timeline from template (S32)
    ...timelineEls,
    // Retry history panel below timeline
    text('ed_rh', cx, cy + 380, 200, 20, 'Retry History', 14, C.gray900),
    rect('ed_rh_panel', cx, cy + 406, 620, 120, C.gray300, C.white, 1, false),
    rect('ed_rh_hdr', cx, cy + 406, 620, 40, 'transparent', C.gray100, 0, false),
    hline('ed_rh_div', cx, cy + 446, 620, C.gray300),
    ...['#1 · Completed · 1 hr ago · Triggered by Alex Brown', '#0 (original) · Failed · 2 hr ago · Triggered by Schedule'].flatMap((row, i) => [
      text(`ed_rh_r_${i}`, cx + 16, cy + 454 + i * 36, 580, 18, row, 12, C.gray700),
    ]),
  ];
  write('E06-workflow-engine/execution-detail.excalidraw', els);
}

// ─── Main ─────────────────────────────────────────────────────────────────────

genAppShell();
genSettingsUsers();
genSettingsRoles();
genSettingsSecurity();
genAcceptInvitation();
genDataModels();
genDataClasses();
genRecords();
genWorkflows();
genWorkflowEditor();
genForms();
genFormEditor();
genFormSubmission();
genExecutions();
genExecutionDetail();

console.log('\n✅  All screen wireframes generated.');
