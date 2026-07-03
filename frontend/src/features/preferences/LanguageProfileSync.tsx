import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';

import { useAuthStore } from '@/features/auth/auth-store';
import { dashboardQueryKeys, getCurrentUserProfile } from '@/features/dashboard/api';
import { changeSiteLanguage } from '@/features/preferences/i18n';
import { DEFAULT_LANGUAGE, isSupportedLanguage } from '@/features/preferences/language-store';

export function LanguageProfileSync() {
  const accessToken = useAuthStore((state) => state.accessToken);
  const profileQuery = useQuery({
    queryKey: dashboardQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
    enabled: Boolean(accessToken),
  });

  useEffect(() => {
    const language = profileQuery.data?.language;
    if (isSupportedLanguage(language)) {
      void changeSiteLanguage(language);
    } else if (profileQuery.isSuccess && language) {
      void changeSiteLanguage(DEFAULT_LANGUAGE);
    }
  }, [profileQuery.data?.language, profileQuery.isSuccess]);

  return null;
}
