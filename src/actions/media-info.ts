import { action, DialAction, DidReceiveSettingsEvent, KeyAction, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from '@elgato/streamdeck';
import { Marquee } from '../utils/marquee';
import { mediaManagerService, toggleMediaPlayPause, nextMedia, previousMedia, type MediaInfo, type MediaManagerError, type MediaManagerResult } from '../utils/media-manager';

type ActionType = 'toggle' | 'next' | 'previous' | 'none';

type MediaInfoSettings = {
	showTitle?: boolean;
	showArtists?: boolean;
	enableMarquee?: boolean;
	position?: 'none' | 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';
	action?: ActionType;
};

@action({ UUID: 'ru.valentderah.media-manager.media-info' })
export class MediaInfoAction extends SingletonAction<MediaInfoSettings> {
	private static readonly DEFAULT_SETTINGS: MediaInfoSettings = {
		showTitle: true,
		showArtists: true,
		enableMarquee: true,
		position: 'none',
		action: 'toggle'
	};

	private static readonly ERROR_MESSAGES: Record<MediaManagerError['type'], string> = {
		FILE_NOT_FOUND: 'Error\nFile Not\nFound',
		HELPER_ERROR: 'Error\nHelper',
		PARSING_ERROR: 'Error\nParsing',
		NOTHING_PLAYING: 'Nothing\nPlaying'
	} as const;

	/**
	 * @param size Default is 144px for one cell
	 */
	private static generatePlaceholderImage(size: number = 144): string {

		const svg = `<svg width="${size}" height="${size}" xmlns="http://www.w3.org/2000/svg">
			<rect width="${size}" height="${size}" fill="#242424"/>
		</svg>`;
		const base64 = Buffer.from(svg).toString('base64');
		return `data:image/svg+xml;base64,${base64}`;
	}

	private readonly actionHandlers = new Map<DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>, {
		settings: MediaInfoSettings;
		titleMarquee: Marquee;
		artistsMarquee: Marquee;
		currentMediaInfo: MediaInfo | null;
		unsubscribe: (() => void) | undefined;
	}>();

	constructor() {
		super();
		mediaManagerService.subscribe(this.handleMediaUpdateForAll.bind(this));
	}

	override async onWillAppear(ev: WillAppearEvent<MediaInfoSettings>): Promise<void> {
		const action = ev.action as DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>;
		const settings = await this.loadSettings(action);

		const handler = {
			settings,
			titleMarquee: new Marquee(),
			artistsMarquee: new Marquee(),
			currentMediaInfo: null as MediaInfo | null,
			unsubscribe: undefined as (() => void) | undefined
		};

		this.actionHandlers.set(action, handler);
		
		const wasProcessRunning = mediaManagerService.isProcessRunning();
		mediaManagerService.start();
		
		setTimeout(() => {
			mediaManagerService.requestUpdate();
		}, wasProcessRunning ? 50 : 200);
	}

	override onWillDisappear(ev: WillDisappearEvent<MediaInfoSettings>): void | Promise<void> {
		const action = ev.action as DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>;
		const handler = this.actionHandlers.get(action);
		
		if (handler) {
			handler.titleMarquee.stop();
			handler.artistsMarquee.stop();

			if (handler.unsubscribe) {
				handler.unsubscribe();
			}
			
			this.actionHandlers.delete(action);
		}

		if (this.actionHandlers.size === 0) {
			mediaManagerService.stop();
		}
	}

	override onKeyDown(ev: KeyDownEvent<MediaInfoSettings>): void {
		const action = ev.action as KeyAction<MediaInfoSettings>;
		const handler = this.actionHandlers.get(action);
		
		if (!handler) return;
		
		const actionType = handler.settings.action ?? MediaInfoAction.DEFAULT_SETTINGS.action ?? 'toggle';
		
		switch (actionType) {
			case 'toggle':
				toggleMediaPlayPause();
				break;
			case 'next':
				nextMedia();
				break;
			case 'previous':
				previousMedia();
				break;
			case 'none':
				break;
		}
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<MediaInfoSettings>): Promise<void> {
		const action = ev.action as DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>;
		const handler = this.actionHandlers.get(action);
		
		if (!handler) return;
		
		handler.settings = {
			showTitle: ev.payload.settings.showTitle ?? MediaInfoAction.DEFAULT_SETTINGS.showTitle,
			showArtists: ev.payload.settings.showArtists ?? MediaInfoAction.DEFAULT_SETTINGS.showArtists,
			enableMarquee: ev.payload.settings.enableMarquee ?? MediaInfoAction.DEFAULT_SETTINGS.enableMarquee,
			position: ev.payload.settings.position ?? MediaInfoAction.DEFAULT_SETTINGS.position,
			action: ev.payload.settings.action ?? MediaInfoAction.DEFAULT_SETTINGS.action
		};


		if (handler.currentMediaInfo) {
			await this.updateAction(action, handler, { success: true, data: handler.currentMediaInfo });
		}
	}

	private async loadSettings(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>): Promise<MediaInfoSettings> {
		const loadedSettings = await action.getSettings();
		return {
			showTitle: loadedSettings.showTitle ?? MediaInfoAction.DEFAULT_SETTINGS.showTitle,
			showArtists: loadedSettings.showArtists ?? MediaInfoAction.DEFAULT_SETTINGS.showArtists,
			enableMarquee: loadedSettings.enableMarquee ?? MediaInfoAction.DEFAULT_SETTINGS.enableMarquee,
			position: loadedSettings.position ?? MediaInfoAction.DEFAULT_SETTINGS.position,
			action: loadedSettings.action ?? MediaInfoAction.DEFAULT_SETTINGS.action
		};
	}


	private async handleMediaUpdateForAll(result: MediaManagerResult): Promise<void> {
		for (const [action, handler] of this.actionHandlers.entries()) {
			await this.updateAction(action, handler, result);
		}
	}

	private async updateAction(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>, handler: {
		settings: MediaInfoSettings;
		titleMarquee: Marquee;
		artistsMarquee: Marquee;
		currentMediaInfo: MediaInfo | null;
		unsubscribe: (() => void) | undefined;
	}, result: MediaManagerResult): Promise<void> {
		const { settings, titleMarquee, artistsMarquee } = handler;

		if (!result.success) {
			handler.currentMediaInfo = null;
			titleMarquee.stop();
			artistsMarquee.stop();
			
			if (result.error.type === 'NOTHING_PLAYING') {
				const placeholderSize = settings.position === 'none' ? 288 : 144;
				const placeholderImage = MediaInfoAction.generatePlaceholderImage(placeholderSize);
				await action.setImage(placeholderImage);
				await action.setTitle('');
			} else {
				await action.setImage('');
				await action.setTitle(MediaInfoAction.ERROR_MESSAGES[result.error.type]);
			}
			return;
		}

		const info = result.data;

		await this.updateActionImage(action, handler, info);

		const titleChanged = handler.currentMediaInfo?.Title !== info.Title;
		const previousArtistText = handler.currentMediaInfo ? this.getArtistText(handler.currentMediaInfo) : '';
		const currentArtistText = this.getArtistText(info);
		const artistsChanged = previousArtistText !== currentArtistText;
		handler.currentMediaInfo = info;

		if (titleChanged && info.Title) {
			titleMarquee.setText(info.Title);
		}

		if (artistsChanged && currentArtistText) {
			artistsMarquee.setText(currentArtistText);
		}

		const updateCallback = () => {
			this.updateMarqueeTitle(action, handler);
		};
		this.updateMarqueeState(handler, updateCallback);

		await this.updateMarqueeTitle(action, handler);
	}

	private updateMarqueeState(handler: {
		settings: MediaInfoSettings;
		titleMarquee: Marquee;
		artistsMarquee: Marquee;
	}, updateCallback: () => void | Promise<void>): void {
		const { settings, titleMarquee, artistsMarquee } = handler;
		
		const shouldRunTitleMarquee = settings.enableMarquee && settings.showTitle;
		if (shouldRunTitleMarquee && !titleMarquee.isRunning()) {
			titleMarquee.start(updateCallback);
		} else if (!shouldRunTitleMarquee && titleMarquee.isRunning()) {
			titleMarquee.stop();
		}

		const shouldRunArtistsMarquee = settings.enableMarquee && settings.showArtists;
		if (shouldRunArtistsMarquee && !artistsMarquee.isRunning()) {
			artistsMarquee.start(updateCallback);
		} else if (!shouldRunArtistsMarquee && artistsMarquee.isRunning()) {
			artistsMarquee.stop();
		}
	}

	private async updateActionImage(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>, handler: {
		settings: MediaInfoSettings;
	}, info: MediaInfo): Promise<void> {
		const { settings } = handler;
		const position = settings.position ?? 'none';

		if (position === 'none') {
			if (info.CoverArtBase64) {
				await action.setImage(`data:image/png;base64,${info.CoverArtBase64}`);
			} else {
				// Если нет обложки, показываем серый плейсхолдер
				const placeholderImage = MediaInfoAction.generatePlaceholderImage(288);
				await action.setImage(placeholderImage);
			}
			return;
		}

		let partBase64: string | undefined;
		switch (position) {
			case 'top-left':
				partBase64 = info.CoverArtPart1Base64;
				break;
			case 'top-right':
				partBase64 = info.CoverArtPart2Base64;
				break;
			case 'bottom-left':
				partBase64 = info.CoverArtPart3Base64;
				break;
			case 'bottom-right':
				partBase64 = info.CoverArtPart4Base64;
				break;
		}


		if (partBase64) {
			await action.setImage(`data:image/png;base64,${partBase64}`);
		} else if (info.CoverArtBase64) {
			await action.setImage(`data:image/png;base64,${info.CoverArtBase64}`);
		} else {
			// Если нет обложки, показываем серый плейсхолдер
			const placeholderImage = MediaInfoAction.generatePlaceholderImage(144);
			await action.setImage(placeholderImage);
		}
	}


	private getArtistText(info: MediaInfo): string {
		if (info.Artists && info.Artists.length > 0) {
			return info.Artists.join(', ');
		}
		return info.Artist || '';
	}

	private async updateMarqueeTitle(action: DialAction<MediaInfoSettings> | KeyAction<MediaInfoSettings>, handler: {
		settings: MediaInfoSettings;
		currentMediaInfo: MediaInfo | null;
		titleMarquee: Marquee;
		artistsMarquee: Marquee;
	}): Promise<void> {
		const { settings, currentMediaInfo, titleMarquee, artistsMarquee } = handler;

		if (!currentMediaInfo) {
			// Устанавливаем серый плейсхолдер и пустой заголовок
			const placeholderSize = settings.position === 'none' ? 288 : 144;
			const placeholderImage = MediaInfoAction.generatePlaceholderImage(placeholderSize);
			await action.setImage(placeholderImage);
			await action.setTitle('');
			return;
		}

		const displayText = this.buildDisplayText(currentMediaInfo, settings, titleMarquee, artistsMarquee);
		await action.setTitle(displayText || '');
	}

	private buildDisplayText(info: MediaInfo, settings: MediaInfoSettings, titleMarquee: Marquee, artistsMarquee: Marquee): string | null {
		const parts: string[] = [];

		if (settings.showArtists) {
			const artistText = this.getArtistText(info);
			if (artistText) {
				if (settings.enableMarquee) {
					parts.push(artistsMarquee.getCurrentFrame());
				} else {
					parts.push(artistText);
				}
			}
		}

		if (settings.showTitle && info.Title) {
			if (settings.enableMarquee) {
				parts.push(titleMarquee.getCurrentFrame());
			} else {
				parts.push(info.Title);
			}
		}

		return parts.length > 0 ? parts.join('\n') : null;
	}
}
