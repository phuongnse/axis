import type { TFunction } from 'i18next';
import type { DataTableMessages } from './types';

export interface DataTableMessageContent {
  searchLabel: string;
  searchPlaceholder: string;
  emptyTitle: string;
  emptyDescription: string;
  errorTitle: string;
  errorDescription: string;
}

export function createDataTableMessages(
  t: TFunction,
  content: DataTableMessageContent,
): DataTableMessages {
  return {
    ...content,
    filters: t('table.filters'),
    addCondition: t('table.addCondition'),
    addFilterGroup: t('table.addFilterGroup'),
    removeCondition: t('table.removeCondition'),
    removeFilterGroup: t('table.removeFilterGroup'),
    filterAnd: t('table.filterAnd'),
    filterOr: t('table.filterOr'),
    filterIncomplete: t('table.filterIncomplete'),
    selectFilterField: t('table.selectFilterField'),
    selectFilterOperator: t('table.selectFilterOperator'),
    selectFilterValue: t('table.selectFilterValue'),
    filterOperators: {
      eq: t('table.operator.eq'),
      ne: t('table.operator.ne'),
      contains: t('table.operator.contains'),
      notContains: t('table.operator.notContains'),
      startsWith: t('table.operator.startsWith'),
      endsWith: t('table.operator.endsWith'),
      lt: t('table.operator.lt'),
      lte: t('table.operator.lte'),
      gt: t('table.operator.gt'),
      gte: t('table.operator.gte'),
      between: t('table.operator.between'),
      notBetween: t('table.operator.notBetween'),
      in: t('table.operator.in'),
      notIn: t('table.operator.notIn'),
      containsAny: t('table.operator.containsAny'),
      containsAll: t('table.operator.containsAll'),
      notContainsAny: t('table.operator.notContainsAny'),
      isEmpty: t('table.operator.isEmpty'),
      isNotEmpty: t('table.operator.isNotEmpty'),
    },
    filterBy: (column) => t('table.filterBy', { column }),
    columns: t('table.columns'),
    grouping: t('table.grouping'),
    clearFilters: t('table.clearFilters'),
    noResultsTitle: t('table.noResultsTitle'),
    noResultsDescription: t('table.noResultsDescription'),
    loading: t('table.loading'),
    retry: t('app.retry'),
    sortAscending: t('table.sortAscending'),
    sortDescending: t('table.sortDescending'),
    clearSorting: t('table.clearSorting'),
    hideColumn: t('table.hideColumn'),
    pinLeft: t('table.pinLeft'),
    pinRight: t('table.pinRight'),
    unpin: t('table.unpin'),
    minimum: t('table.minimum'),
    maximum: t('table.maximum'),
    trueValue: t('table.trueValue'),
    falseValue: t('table.falseValue'),
    allValues: t('table.allValues'),
    rowsPerPage: t('table.rowsPerPage'),
    pageStatus: (page, pageCount) => t('table.pageStatus', { page, pageCount }),
    rowStatus: (visible, total) => t('table.rowStatus', { visible, total }),
    selectedStatus: (selected, total) => t('table.selectedStatus', { selected, total }),
    firstPage: t('table.firstPage'),
    previousPage: t('table.previousPage'),
    nextPage: t('table.nextPage'),
    lastPage: t('table.lastPage'),
    loadMore: t('table.loadMore'),
    loadingMore: t('table.loadingMore'),
    endOfList: t('table.endOfList'),
    selectAllRows: t('table.selectAllRows'),
    selectRow: t('table.selectRow'),
    expandRow: t('table.expandRow'),
    collapseRow: t('table.collapseRow'),
  };
}
