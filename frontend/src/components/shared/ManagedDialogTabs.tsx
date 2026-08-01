import type { ReactNode } from 'react';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

type ManagedDialogBusinessSection = {
  id: string;
  label: string;
  content: ReactNode;
};

type ManagedDialogSystemInfo = {
  label: string;
  content: ReactNode;
};

function ManagedDialogTabs({
  label,
  generalLabel,
  general,
  sections = [],
  systemInfo,
  activeSection,
  defaultSection = 'general',
  onActiveSectionChange,
}: {
  label: string;
  generalLabel: string;
  general: ReactNode;
  sections?: readonly ManagedDialogBusinessSection[];
  systemInfo?: ManagedDialogSystemInfo;
  activeSection?: string;
  defaultSection?: string;
  onActiveSectionChange?: (section: string) => void;
}) {
  const allSections = [
    { id: 'general', label: generalLabel, content: general },
    ...sections,
    ...(systemInfo ? [{ id: 'system-info', ...systemInfo }] : []),
  ];

  if (allSections.length === 1) {
    return (
      <div data-slot="managed-dialog-tabs" className="min-w-0">
        <div data-slot="managed-dialog-tab-panel" className="pt-4">
          {general}
        </div>
      </div>
    );
  }

  return (
    <div data-slot="managed-dialog-tabs" className="min-w-0">
      <Tabs
        className="min-w-0 max-w-full"
        {...(activeSection === undefined
          ? { defaultValue: defaultSection }
          : { value: activeSection })}
        onValueChange={(value) => {
          if (typeof value === 'string') onActiveSectionChange?.(value);
        }}
      >
        <div
          data-slot="managed-dialog-tab-scroll"
          className="min-w-0 max-w-full touch-pan-x overflow-x-auto overflow-y-hidden pb-2 sm:overflow-x-clip"
        >
          <TabsList variant="line" aria-label={label} className="min-w-max">
            {allSections.map((section) => (
              <TabsTrigger key={section.id} value={section.id}>
                {section.label}
              </TabsTrigger>
            ))}
          </TabsList>
        </div>
        {allSections.map((section) => (
          <TabsContent
            key={section.id}
            value={section.id}
            keepMounted
            data-slot="managed-dialog-tab-panel"
            className="pt-4"
          >
            {section.content}
          </TabsContent>
        ))}
      </Tabs>
    </div>
  );
}

export type { ManagedDialogBusinessSection, ManagedDialogSystemInfo };
export { ManagedDialogTabs };
