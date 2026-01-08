import { action, DialAction, DidReceiveSettingsEvent, KeyAction, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from '@elgato/streamdeck';
import { Marquee } from '../utils/marquee';
import { mediaManagerService, toggleMediaPlayPause, type MediaInfo, type MediaManagerError, type MediaManagerResult } from '../utils/media-manager';

type MediaInfoSettings = {
	showTitle?: boolean;
	showArtists?: boolean;
	enableMarquee?: boolean;
};

@action({ UUID: 'ru.valentderah.media-manager.media-info' })
export class MediaInfoAction extends SingletonAction<MediaInfoSettings> {
	private static readonly DEFAULT_SETTINGS: MediaInfoSettings = {
		showTitle: true,
		showArtists: true,
		enableMarquee: true
	};

	private static readonly ERROR_MESSAGES: Record<MediaManagerError['type'], string> = {
		FILE_NOT_FOUND: 'Error\nFile Not\nFound',
		HELPER_ERROR: 'Error\nHelper',
		PARSING_ERROR: 'Error\nParsing',
		NOTHING_PLAYING: 'Nothing\nPlaying'
	} as const;

	private currentAction: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings> | undefined;
	private readonly titleMarquee: Marquee;
	private readonly artistsMarquee: Marquee;
	private currentMediaInfo: MediaInfo | null = null;
	private settings: MediaInfoSettings = { ...MediaInfoAction.DEFAULT_SETTINGS };

	constructor() {
		super();
		this.titleMarquee = new Marquee();
		this.artistsMarquee = new Marquee();
		// @ts-expect-error - Re-assigning the onUpdate handler
		mediaManagerService.onUpdate = this.handleMediaUpdate.bind(this);
	}

	override async onWillAppear(ev: WillAppearEvent<MediaInfoSettings>): Promise<void> {
		this.currentAction = ev.action;
		await this.loadSettings(ev.action);
		mediaManagerService.start();
	}

	override onWillDisappear(ev: WillDisappearEvent<MediaInfoSettings>): void | Promise<void> {
		mediaManagerService.stop();
		this.titleMarquee.stop();
		this.artistsMarquee.stop();
		this.currentAction = undefined;
		this.currentMediaInfo = null;
	}

	override onKeyDown(ev: KeyDownEvent<MediaInfoSettings>): void {
		toggleMediaPlayPause();
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<MediaInfoSettings>): Promise<void> {
		
		this.settings = {
			showTitle: ev.payload.settings.showTitle ?? MediaInfoAction.DEFAULT_SETTINGS.showTitle,
			showArtists: ev.payload.settings.showArtists ?? MediaInfoAction.DEFAULT_SETTINGS.showArtists,
			enableMarquee: ev.payload.settings.enableMarquee ?? MediaInfoAction.DEFAULT_SETTINGS.enableMarquee
		};

		const updateCallback = () => {
			this.currentAction && this.updateMarqueeTitle(this.currentAction);
		};
		this.updateMarqueeState(updateCallback);
		await this.updateMarqueeTitle(this.currentAction);
	}

	private async loadSettings(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>): Promise<void> {
		const loadedSettings = await action.getSettings();
		this.settings = {
			showTitle: loadedSettings.showTitle ?? MediaInfoAction.DEFAULT_SETTINGS.showTitle,
			showArtists: loadedSettings.showArtists ?? MediaInfoAction.DEFAULT_SETTINGS.showArtists,
			enableMarquee: loadedSettings.enableMarquee ?? MediaInfoAction.DEFAULT_SETTINGS.enableMarquee
		};
	}

	private updateMarqueeState(updateCallback: () => void): void {
		const shouldRunTitleMarquee = this.settings.enableMarquee && this.settings.showTitle;
		if (shouldRunTitleMarquee && !this.titleMarquee.isRunning()) {
			this.titleMarquee.start(updateCallback);
		} else if (!shouldRunTitleMarquee && this.titleMarquee.isRunning()) {
			this.titleMarquee.stop();
		}

		const shouldRunArtistsMarquee = this.settings.enableMarquee && this.settings.showArtists;
		if (shouldRunArtistsMarquee && !this.artistsMarquee.isRunning()) {
			this.artistsMarquee.start(updateCallback);
		} else if (!shouldRunArtistsMarquee && this.artistsMarquee.isRunning()) {
			this.artistsMarquee.stop();
		}
	}

	private async handleMediaUpdate(result: MediaManagerResult): Promise<void> {
		if (!this.currentAction) return;
		const action = this.currentAction;

		if (!result.success) {
			this.currentMediaInfo = null;
			this.titleMarquee.stop();
			this.artistsMarquee.stop();
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
		const previousArtistText = this.currentMediaInfo ? this.getArtistText(this.currentMediaInfo) : '';
		const currentArtistText = this.getArtistText(info);
		const artistsChanged = previousArtistText !== currentArtistText;
		this.currentMediaInfo = info;

		if (titleChanged && info.Title) {
			this.titleMarquee.setText(info.Title);
		}

		if (artistsChanged && currentArtistText) {
			this.artistsMarquee.setText(currentArtistText);
		}

		const updateCallback = () => {
			this.currentAction && this.updateMarqueeTitle(this.currentAction);
		};
		this.updateMarqueeState(updateCallback);

		await this.updateMarqueeTitle(action);
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
				if (this.settings.enableMarquee) {
					parts.push(this.artistsMarquee.getCurrentFrame());
				} else {
					parts.push(artistText);
				}
			}
		}

		if (this.settings.showTitle && info.Title) {
			if (this.settings.enableMarquee) {
				parts.push(this.titleMarquee.getCurrentFrame());
			} else {
				parts.push(info.Title);
			}
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
