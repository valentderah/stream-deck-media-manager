import { action, DialAction, DidReceiveSettingsEvent, KeyAction, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from '@elgato/streamdeck';
import { Marquee } from '../utils/marquee';
import { getMediaInfo, toggleMediaPlayPause, type MediaInfo, type MediaManagerErrorType } from '../utils/media-manager';

type MediaInfoSettings = {
	showTitle?: boolean;
	showArtists?: boolean;
};

@action({ UUID: 'ru.valentderah.media-manager.media-info' })
export class MediaInfoAction extends SingletonAction<MediaInfoSettings> {
	private static readonly UPDATE_INTERVAL_MS = 1000;

	private static readonly DEFAULT_SETTINGS: MediaInfoSettings = {
		showTitle: true,
		showArtists: true
	};

	private static readonly ERROR_MESSAGES: Record<MediaManagerErrorType, string> = {
		FILE_NOT_FOUND: 'Error\nFile Not\nFound',
		HELPER_ERROR: 'Error\nHelper',
		PARSING_ERROR: 'Error\nParsing',
		NOTHING_PLAYING: 'Nothing\nPlaying'
	} as const;

	private intervalId: NodeJS.Timeout | undefined;
	private currentAction: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings> | undefined;
	private readonly titleMarquee: Marquee;
	private currentMediaInfo: MediaInfo | null = null;
	private settings: MediaInfoSettings = { ...MediaInfoAction.DEFAULT_SETTINGS };

	constructor() {
		super();
		this.titleMarquee = new Marquee();
	}

	override async onWillAppear(ev: WillAppearEvent<MediaInfoSettings>): Promise<void> {
		this.currentAction = ev.action;
		await this.loadSettings(ev.action);
		await this.updateMediaInfo(ev.action);
		this.startUpdateInterval();
		if (this.settings.showTitle) {
			this.titleMarquee.start(() => {
				if (this.currentAction) {
					this.updateMarqueeTitle(this.currentAction);
				}
			});
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<MediaInfoSettings>): void | Promise<void> {
		this.stopUpdateInterval();
		this.titleMarquee.stop();
		this.currentAction = undefined;
		this.currentMediaInfo = null;
	}

	override async onKeyDown(ev: KeyDownEvent<MediaInfoSettings>): Promise<void> {
		await toggleMediaPlayPause();
		await this.updateMediaInfo(ev.action);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<MediaInfoSettings>): Promise<void> {
		const wasTitleEnabled = this.settings.showTitle;
		this.settings = {
			showTitle: ev.payload.settings.showTitle ?? MediaInfoAction.DEFAULT_SETTINGS.showTitle,
			showArtists: ev.payload.settings.showArtists ?? MediaInfoAction.DEFAULT_SETTINGS.showArtists
		};

		if (this.settings.showTitle && !wasTitleEnabled) {
			if (this.currentAction) {
				this.titleMarquee.start(() => {
					if (this.currentAction) {
						this.updateMarqueeTitle(this.currentAction);
					}
				});
			}
		} else if (!this.settings.showTitle && wasTitleEnabled) {
			this.titleMarquee.stop();
		}

		if (this.currentAction) {
			await this.updateMarqueeTitle(this.currentAction);
		}
	}

	private async loadSettings(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>): Promise<void> {
		const loadedSettings = await action.getSettings();
		this.settings = {
			showTitle: loadedSettings.showTitle ?? MediaInfoAction.DEFAULT_SETTINGS.showTitle,
			showArtists: loadedSettings.showArtists ?? MediaInfoAction.DEFAULT_SETTINGS.showArtists
		};
	}

	private async updateMediaInfo(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>): Promise<void> {
		const result = await getMediaInfo();

		if (!result.success) {
			this.currentMediaInfo = null;
			this.titleMarquee.stop();
			await action.setImage('');
			await action.setTitle(MediaInfoAction.ERROR_MESSAGES[result.error.type]);
			return;
		}

		const info = result.data;

		if (info.CoverArtBase64) {
			await action.setImage(`data:image/png;base64,${info.CoverArtBase64}`);
		} else {
			await action.setImage('');
		}

		const titleChanged = this.currentMediaInfo?.Title !== info.Title;
		this.currentMediaInfo = info;

		if (titleChanged && info.Title) {
			this.titleMarquee.setText(info.Title);
		}

		await this.updateMarqueeTitle(action);
	}

	private startUpdateInterval(): void {
		this.stopUpdateInterval();
		this.intervalId = setInterval(() => {
			if (this.currentAction) {
				this.updateMediaInfo(this.currentAction);
			}
		}, MediaInfoAction.UPDATE_INTERVAL_MS);
	}

	private stopUpdateInterval(): void {
		if (this.intervalId) {
			clearInterval(this.intervalId);
			this.intervalId = undefined;
		}
	}


	private getArtistText(info: MediaInfo): string {
		if (info.Artists && info.Artists.length > 0) {
			return info.Artists.join(', ');
		}
		return info.Artist || '';
	}

	private buildDisplayText(info: MediaInfo): string | null {
		const parts: string[] = [];

		if (this.settings.showArtists) {
			const artistText = this.getArtistText(info);
			if (artistText) {
				parts.push(artistText);
			}
		}

		if (this.settings.showTitle && info.Title) {
			parts.push(this.titleMarquee.getCurrentFrame());
		}

		return parts.length > 0 ? parts.join('\n') : null;
	}

	private async updateMarqueeTitle(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings> | undefined): Promise<void> {
		if (!action) {
			return;
		}

		if (!this.currentMediaInfo) {
			await action.setTitle(MediaInfoAction.ERROR_MESSAGES.NOTHING_PLAYING);
			return;
		}

		const displayText = this.buildDisplayText(this.currentMediaInfo);
		await action.setTitle(displayText || '');
	}
}