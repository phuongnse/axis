import { FolderPlus, Plus, Trash2 } from 'lucide-react';
import { type ChangeEvent, type JSX, useId } from 'react';
import type {
  ActionProps,
  Option,
  OptionList,
  ValueEditorProps,
  VersatileSelectorProps,
} from 'react-querybuilder';
import { isOptionGroupArray } from 'react-querybuilder';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

export function ShadcnQueryBuilderAction({
  handleOnClick,
  label,
  title,
  disabled,
  testID,
}: ActionProps): JSX.Element {
  const remove = testID?.includes('remove') ?? false;
  const group = testID?.includes('group') ?? false;
  const Icon = remove ? Trash2 : group ? FolderPlus : Plus;
  return (
    <Button
      type="button"
      variant={remove ? 'ghost' : 'outline'}
      size={remove ? 'icon-sm' : 'sm'}
      aria-label={remove ? title : undefined}
      title={title}
      disabled={disabled}
      onClick={handleOnClick}
    >
      <Icon aria-hidden />
      {remove ? null : label}
    </Button>
  );
}

export function ShadcnQueryBuilderSelector({
  className,
  handleOnChange,
  options,
  value,
  title,
  disabled,
  multiple,
  testID,
}: VersatileSelectorProps): JSX.Element {
  const flatOptions = flattenOptions(options as OptionList);
  const selected = Array.isArray(value) ? value.map(String) : value ? [String(value)] : [];
  const selectedLabels = selected.flatMap((selectedValue) => {
    const option = flatOptions.find((candidate) => candidate.name === selectedValue);
    return option ? [String(option.label)] : [];
  });

  if (multiple) {
    return (
      <Popover>
        <PopoverTrigger
          render={
            <Button
              type="button"
              variant="outline"
              size="sm"
              className={className}
              title={title}
              disabled={disabled}
              data-testid={testID}
            />
          }
        >
          {selectedLabels.length > 0 ? selectedLabels.join(', ') : title}
        </PopoverTrigger>
        <PopoverContent align="start" className="max-h-72 w-64 overflow-y-auto p-2">
          <div className="grid gap-1">
            {flatOptions.map((option) => (
              <MultiSelectOption
                key={option.name}
                option={option}
                checked={selected.includes(option.name)}
                onCheckedChange={(checked) =>
                  handleOnChange(
                    checked
                      ? [...selected, option.name]
                      : selected.filter((item) => item !== option.name),
                  )
                }
              />
            ))}
          </div>
        </PopoverContent>
      </Popover>
    );
  }

  return (
    <Select
      value={selected[0] ?? ''}
      disabled={disabled}
      onValueChange={(nextValue) => {
        if (nextValue !== null) handleOnChange(nextValue);
      }}
    >
      <SelectTrigger data-testid={testID} className={className} title={title}>
        <SelectValue>{selectedLabels[0] ?? title}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        {isOptionGroupArray(options as OptionList)
          ? (options as ReturnType<typeof groupedOptions>).map((group) => (
              <SelectGroup key={group.label}>
                <SelectLabel>{group.label}</SelectLabel>
                {group.options.map((option) => (
                  <SelectItem key={option.name} value={option.name}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectGroup>
            ))
          : flatOptions.map((option) => (
              <SelectItem key={option.name} value={option.name}>
                {option.label}
              </SelectItem>
            ))}
      </SelectContent>
    </Select>
  );
}

function MultiSelectOption({
  option,
  checked,
  onCheckedChange,
}: {
  option: Option;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
}) {
  const id = useId();
  return (
    <Label htmlFor={id} className="min-h-9 rounded-sm px-2 hover:bg-accent">
      <Checkbox
        id={id}
        checked={checked}
        onCheckedChange={(value) => onCheckedChange(Boolean(value))}
      />
      {option.label}
    </Label>
  );
}

export function ShadcnQueryBuilderValueEditor(props: ValueEditorProps): JSX.Element | null {
  const {
    operator,
    value,
    handleOnChange,
    type,
    inputType,
    values = [],
    disabled,
    testID,
    className,
    title,
    separator,
  } = props;
  if (operator === 'isEmpty' || operator === 'isNotEmpty') return null;
  if (type === 'select' || type === 'multiselect') {
    return (
      <ShadcnQueryBuilderSelector {...props} options={values} multiple={type === 'multiselect'} />
    );
  }
  if (operator === 'between' || operator === 'notBetween') {
    const current = Array.isArray(value) ? value : ['', ''];
    return (
      <span data-testid={testID} className="flex items-center gap-2">
        {[0, 1].map((index) => (
          <span key={index === 0 ? 'minimum' : 'maximum'} className="contents">
            {index === 1 ? separator : null}
            <Input
              type={inputType ?? 'text'}
              value={current[index] ?? ''}
              disabled={disabled}
              title={title}
              onChange={(event) => {
                const next = [...current];
                next[index] = editorValue(event, inputType);
                handleOnChange(next);
              }}
            />
          </span>
        ))}
      </span>
    );
  }
  const list = operator === 'in' || operator === 'notIn';
  return (
    <Input
      data-testid={testID}
      type={list ? 'text' : (inputType ?? 'text')}
      className={className}
      value={list && Array.isArray(value) ? value.join(', ') : (value ?? '')}
      disabled={disabled}
      title={title}
      onChange={(event) =>
        handleOnChange(
          list
            ? event.target.value
                .split(',')
                .map((item) => item.trim())
                .filter(Boolean)
            : editorValue(event, inputType),
        )
      }
    />
  );
}

function editorValue(event: ChangeEvent<HTMLInputElement>, inputType?: string | null) {
  if (inputType !== 'number') return event.target.value;
  return event.target.value === '' ? '' : Number(event.target.value);
}

function flattenOptions(options: OptionList): Option[] {
  return isOptionGroupArray(options) ? options.flatMap((group) => group.options) : options;
}

function groupedOptions(options: OptionList) {
  return isOptionGroupArray(options) ? options : [];
}
